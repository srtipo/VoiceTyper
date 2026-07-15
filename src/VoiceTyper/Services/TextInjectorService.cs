using System.Windows;
using VoiceTyper.Models;
using VoiceTyper.Native;

namespace VoiceTyper.Services;

public sealed class TextInjectorService
{
    private readonly TrayIconService _tray;
    private readonly SettingsService _settings;

    public TextInjectorService(TrayIconService tray, SettingsService settings)
    {
        _tray = tray;
        _settings = settings;
    }

    public async Task InjectAsync(string text)
    {
        Log.Info($"[Inject] injecting {text.Length} chars");

        try
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is not null)
            {
                var sent = await dispatcher.InvokeAsync(() => SendInputInterop.SendText(text));
                if (sent > 0)
                {
                    Log.Info($"[Inject] injected via SendInput Unicode ({sent} events)");
                    return;
                }
                Log.Warn("[Inject] SendInput Unicode returned 0, falling back to clipboard paste");
            }

            var backup = await ClipboardInterop.BackupAsync();
            var setOk = await ClipboardInterop.SetTextAsync(text);
            if (!setOk)
            {
                Log.Error("[Inject] clipboard SetText failed after retries");
                _tray.ShowBalloon("VoiceTyper", "Clipboard ocupado, reintentá");
                return;
            }

            Log.Info("[Inject] clipboard set, sending WM_PASTE");
            await Task.Delay(50);

            if (dispatcher is not null)
            {
                await dispatcher.InvokeAsync(() => ClipboardInjector.SendPaste());
            }
            else
            {
                ClipboardInjector.SendPaste();
            }

            await Task.Delay(200);

            if (_settings.Current.RestoreClipboard)
            {
                await ClipboardInterop.RestoreAsync(backup);
                Log.Info("[Inject] clipboard restored");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Inject] failed: {ex.Message}");
            var d = Application.Current?.Dispatcher;
            if (d is not null)
            {
                if (d.CheckAccess())
                {
                    _tray.SetState(RecordingState.Error);
                }
                else
                {
                    _ = d.BeginInvoke(() => _tray.SetState(RecordingState.Error));
                }
            }
        }
    }
}
