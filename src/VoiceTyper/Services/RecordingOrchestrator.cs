using System.Windows;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public sealed class RecordingOrchestrator : IDisposable
{
    private readonly HotkeyService _hotkey;
    private readonly AudioRecorderService _audio;
    private readonly TrayIconService _tray;
    private readonly TranscriberService _transcriber;
    private readonly SettingsService _settings;
    private readonly LoggerService _logger;
    private readonly TextInjectorService _injector;
    private readonly CursorIndicatorService _indicator;
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);
    private bool _disposed;

    public RecordingOrchestrator(
        HotkeyService hotkey,
        AudioRecorderService audio,
        TrayIconService tray,
        TranscriberService transcriber,
        SettingsService settings,
        LoggerService logger,
        TextInjectorService injector,
        CursorIndicatorService indicator)
    {
        _hotkey = hotkey;
        _audio = audio;
        _tray = tray;
        _transcriber = transcriber;
        _settings = settings;
        _logger = logger;
        _injector = injector;
        _indicator = indicator;
        _hotkey.RecordingStarted += OnRecordingStarted;
        _hotkey.RecordingStopped += () => _ = OnRecordingStoppedAsync();
    }

    private void OnRecordingStarted()
    {
        if (!_processingSemaphore.Wait(0))
        {
            Log.Warn("[Orchestrator] previous recording still processing, dropping new recording start");
            DispatcherUi(() => _tray.ShowBalloon("VoiceTyper", "Esperá a que termine la transcripción anterior"));
            return;
        }

        Log.Info("[Orchestrator] recording started");
        _audio.DeviceNumber = _settings.Current.MicrophoneDeviceIndex;
        SetUiState(RecordingState.Recording);

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"[Orchestrator] start failed: {ex.Message}");
            SetUiState(RecordingState.Error);
            _processingSemaphore.Release();
            DispatcherUi(() => _tray.ShowBalloon("VoiceTyper — Error", "No se pudo iniciar la captura de audio."));
        }
    }

    private async Task OnRecordingStoppedAsync()
    {
        try
        {
            Log.Info("[Orchestrator] recording stopped");
            SetUiState(RecordingState.Processing);

            byte[] wav;
            try
            {
                wav = await _audio.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"[Orchestrator] stop failed: {ex.Message}");
                SetUiState(RecordingState.Error);
                return;
            }

            if (wav.Length == 0)
            {
                Log.Info("[Orchestrator] empty capture, back to idle");
                SetUiState(RecordingState.Idle);
                return;
            }

            SetUiState(RecordingState.Processing);
            string text;
            try
            {
                text = await _transcriber.TranscribeAsync(wav, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex);
                SetUiState(RecordingState.Error);
                await Task.Delay(2000).ConfigureAwait(false);
                SetUiState(RecordingState.Idle);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Info("[Orchestrator] transcription produced no text, back to idle");
                SetUiState(RecordingState.Idle);
                return;
            }

            Log.Info($"[Orchestrator] transcribed: {text}");

            try
            {
                await _injector.InjectAsync(text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex);
            }

            SetUiState(RecordingState.Idle);
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    private void SetUiState(RecordingState state)
    {
        DispatcherUi(() =>
        {
            _tray.SetState(state);
            if (state == RecordingState.Idle)
            {
                _indicator.Hide();
            }
            else
            {
                _indicator.Show(state);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkey.RecordingStarted -= OnRecordingStarted;
        _hotkey.RecordingStopped -= () => _ = OnRecordingStoppedAsync();
        _indicator.Hide();
        _processingSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void DispatcherUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess())
        {
            a();
        }
        else
        {
            d.BeginInvoke(a);
        }
    }
}
