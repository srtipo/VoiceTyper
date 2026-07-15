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
    private bool _disposed;

    public RecordingOrchestrator(
        HotkeyService hotkey,
        AudioRecorderService audio,
        TrayIconService tray,
        TranscriberService transcriber,
        SettingsService settings,
        LoggerService logger,
        TextInjectorService injector)
    {
        _hotkey = hotkey;
        _audio = audio;
        _tray = tray;
        _transcriber = transcriber;
        _settings = settings;
        _logger = logger;
        _injector = injector;
        _hotkey.RecordingStarted += OnRecordingStarted;
        _hotkey.RecordingStopped += () => _ = OnRecordingStoppedAsync();
    }

    private void OnRecordingStarted()
    {
        Log.Info("[Orchestrator] recording started");
        Dispatcher(() => _tray.SetState(RecordingState.Recording));

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"[Orchestrator] start failed: {ex.Message}");
            Dispatcher(() => _tray.SetState(RecordingState.Error));
            Dispatcher(() => _tray.ShowBalloon("VoiceTyper — Error", "No se pudo iniciar la captura de audio."));
        }
    }

    private async Task OnRecordingStoppedAsync()
    {
        Log.Info("[Orchestrator] recording stopped");
        Dispatcher(() => _tray.SetState(RecordingState.Processing));

        byte[] wav;
        try
        {
            wav = await _audio.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"[Orchestrator] stop failed: {ex.Message}");
            Dispatcher(() => _tray.SetState(RecordingState.Error));
            return;
        }

        if (wav.Length == 0)
        {
            Log.Info("[Orchestrator] empty capture, back to idle");
            Dispatcher(() => _tray.SetState(RecordingState.Idle));
            return;
        }

        Dispatcher(() => _tray.SetState(RecordingState.Processing));
        string text;
        try
        {
            text = await _transcriber.TranscribeAsync(wav, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex);
            Dispatcher(() => _tray.SetState(RecordingState.Error));
            await Task.Delay(2000).ConfigureAwait(false);
            Dispatcher(() => _tray.SetState(RecordingState.Idle));
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Info("[Orchestrator] transcription produced no text, back to idle");
            Dispatcher(() => _tray.SetState(RecordingState.Idle));
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

        Dispatcher(() => _tray.SetState(RecordingState.Idle));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkey.RecordingStarted -= OnRecordingStarted;
        _hotkey.RecordingStopped -= () => _ = OnRecordingStoppedAsync();
        GC.SuppressFinalize(this);
    }

    private static void Dispatcher(Action a)
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
