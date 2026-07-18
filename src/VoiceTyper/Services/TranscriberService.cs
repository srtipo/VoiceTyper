using System;
using System.IO;
using VoiceTyper.Models;
using Whisper.net;

namespace VoiceTyper.Services;

public sealed class TranscriberService : IDisposable
{
    private readonly ModelManagerService _modelManager;
    private readonly SettingsService _settings;
    private readonly LoggerService _logger;
    private readonly CudaDetector _cudaDetector;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WhisperModel? _loadedModel;
    private string? _loadedLanguage;
    private bool? _loadedGpu;
    private bool _disposed;

    public string BackendMode { get; private set; } = "CPU";

    public TranscriberService(
        ModelManagerService modelManager,
        SettingsService settings,
        LoggerService logger,
        CudaDetector cudaDetector)
    {
        _modelManager = modelManager;
        _settings = settings;
        _logger = logger;
        _cudaDetector = cudaDetector;
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        var startTs = System.Diagnostics.Stopwatch.GetTimestamp();
        var model = _settings.Current.Model;
        var language = _settings.Current.Language;
        var useGpu = ResolveUseGpu(out var gpuDeviceIndex, out var deviceOutOfRange);

        if (deviceOutOfRange)
        {
            Log.Warn($"[Transcriber] configured GpuDeviceIndex {gpuDeviceIndex} out of range, using device 0");
            gpuDeviceIndex = 0;
        }

        Log.Info($"[Transcriber] start: model={model} lang={language} useGpu={useGpu} bytes={wavBytes.Length}");

        var needRebuild = _processor is null
            || _loadedModel != model
            || _loadedLanguage != language
            || _loadedGpu != useGpu;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (needRebuild)
            {
                Log.Info($"[Transcriber] cache miss: processor_null={_processor is null} model_changed={_loadedModel != model} lang_changed={_loadedLanguage != language} gpu_changed={_loadedGpu != useGpu}");

                if (_processor is not null)
                {
                    try { _processor.Dispose(); } catch { }
                    _processor = null;
                }
                if (_factory is not null)
                {
                    try { _factory.Dispose(); } catch { }
                    _factory = null;
                }

                var modelPath = await _modelManager.EnsureModelAsync(model, progress: null, ct).ConfigureAwait(false);
                var factoryStartTs = System.Diagnostics.Stopwatch.GetTimestamp();
                _factory = TryCreateFactory(modelPath, useGpu, gpuDeviceIndex);

                var strategyBuilder = _factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithGreedySamplingStrategy();
                _processor = strategyBuilder.ParentBuilder.Build();
                var factoryMs = System.Diagnostics.Stopwatch.GetElapsedTime(factoryStartTs).TotalMilliseconds;

                _loadedModel = model;
                _loadedLanguage = language;
                _loadedGpu = useGpu;
                Log.Info($"[Transcriber] factory ready: backend={BackendMode} build_ms={factoryMs:F0}");
            }
            else
            {
                Log.Info($"[Transcriber] cache hit: backend={BackendMode}");
            }
        }
        finally
        {
            _initLock.Release();
        }

        var processor = _processor!;
        var segments = new List<string>();
        await using var ms = new MemoryStream(wavBytes, writable: false);
        var processStartTs = System.Diagnostics.Stopwatch.GetTimestamp();
        await foreach (var segment in processor.ProcessAsync(ms, ct))
        {
            if (string.IsNullOrWhiteSpace(segment.Text)) continue;
            var trimmed = segment.Text.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) continue;
            segments.Add(trimmed);
        }
        var processMs = System.Diagnostics.Stopwatch.GetElapsedTime(processStartTs).TotalMilliseconds;

        var totalMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
        Log.Info($"[Transcriber] end: total_ms={totalMs:F0} process_ms={processMs:F0} segments={segments.Count}");

        return string.Join(' ', segments);
    }

    private bool ResolveUseGpu(out int deviceIndex, out bool deviceOutOfRange)
    {
        deviceOutOfRange = false;
        deviceIndex = _settings.Current.GpuDeviceIndex;

        if (!_settings.Current.GpuEnabled) return false;
        if (!_cudaDetector.IsAvailable()) return false;

        var count = _cudaDetector.GetDeviceCount();
        if (count <= 0) return false;

        if (deviceIndex < 0 || deviceIndex >= count)
        {
            deviceOutOfRange = true;
            deviceIndex = 0;
        }
        return true;
    }

    private WhisperFactory TryCreateFactory(string modelPath, bool useGpu, int deviceIndex)
    {
        if (useGpu)
        {
            try
            {
                var gpuOptions = new WhisperFactoryOptions
                {
                    UseGpu = true,
                    GpuDevice = deviceIndex
                };
                var factory = WhisperFactory.FromPath(modelPath, gpuOptions);
                BackendMode = $"GPU:{deviceIndex}";
                return factory;
            }
            catch (Exception ex)
            {
                Log.Warn($"[Transcriber] GPU init failed: {ex.GetType().Name}: {ex.Message}, falling back to CPU");
                BackendMode = "CPU";
            }
        }

        BackendMode = "CPU";
        return WhisperFactory.FromPath(modelPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _processor?.Dispose(); } catch { }
        try { _factory?.Dispose(); } catch { }
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public Task<bool> SmokeTestAsync()
    {
        return Task.Run(() =>
        {
            string? modelPath = null;
            try
            {
                modelPath = FindExistingModel();
                if (modelPath is null)
                {
                    modelPath = CreateDummyModel();
                }

                var cpuOptions = new WhisperFactoryOptions { UseGpu = false };
                using var factory = WhisperFactory.FromPath(modelPath, cpuOptions);
                Log.Info($"[SmokeTest] OK - WhisperFactory loaded from {modelPath} (CPU)");
                return true;
            }
            catch (DllNotFoundException ex)
            {
                Log.Error($"[SmokeTest] FAIL - native libs missing: {ex.Message}");
                return false;
            }
            catch (FileNotFoundException ex)
            {
                Log.Error($"[SmokeTest] FAIL - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SmokeTest] FAIL - {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        });
    }

    private string? FindExistingModel()
    {
        var dir = _modelManager.ModelDir;
        if (!Directory.Exists(dir)) return null;

        foreach (var file in Directory.EnumerateFiles(dir, "ggml-*.bin"))
        {
            return file;
        }
        return null;
    }

    private string CreateDummyModel()
    {
        var path = Path.Combine(Path.GetTempPath(), "vt_smoke_test_model.bin");
        File.WriteAllBytes(path, new byte[] { 0x00 });
        return path;
    }
}
