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
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WhisperModel? _loadedModel;
    private string? _loadedLanguage;
    private bool _disposed;

    public TranscriberService(ModelManagerService modelManager, SettingsService settings, LoggerService logger)
    {
        _modelManager = modelManager;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        var model = _settings.Current.Model;
        var language = _settings.Current.Language;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_processor is null || _loadedModel != model || _loadedLanguage != language)
            {
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
                _factory = WhisperFactory.FromPath(modelPath);
                var strategyBuilder = _factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithGreedySamplingStrategy();
                _processor = strategyBuilder.ParentBuilder.Build();
                _loadedModel = model;
                _loadedLanguage = language;
                _logger.LogInfo($"model {model} loaded (lang={language})");
            }
        }
        finally
        {
            _initLock.Release();
        }

        var processor = _processor!;
        var segments = new List<string>();
        await using var ms = new MemoryStream(wavBytes, writable: false);
        await foreach (var segment in processor.ProcessAsync(ms, ct))
        {
            if (string.IsNullOrWhiteSpace(segment.Text)) continue;
            var trimmed = segment.Text.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) continue;
            segments.Add(trimmed);
        }

        return string.Join(' ', segments);
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

                using var factory = WhisperFactory.FromPath(modelPath);
                Log.Info($"[SmokeTest] OK - WhisperFactory loaded from {modelPath}");
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
