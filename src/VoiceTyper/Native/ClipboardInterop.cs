using System.Windows;
using VoiceTyper.Services;

namespace VoiceTyper.Native;

public static class ClipboardInterop
{
    public static Task<IDataObject?> BackupAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<IDataObject?>(null);
        }
        IDataObject? Get()
        {
            return Clipboard.GetDataObject();
        }
        return dispatcher.InvokeAsync(Get).Task;
    }

    public static async Task<bool> SetTextAsync(string text, int retries = 3, int backoffMs = 100)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Log.Error("[Inject] dispatcher unavailable for SetText");
            return false;
        }

        for (var attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                Log.Info($"[Inject] SetText attempt {attempt}/{retries}");
                await dispatcher.InvokeAsync(() => Clipboard.SetDataObject(text, copy: true));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Inject] SetText attempt {attempt} failed: {ex.Message}");
                if (attempt < retries)
                {
                    await Task.Delay(backoffMs);
                }
            }
        }

        return false;
    }

    public static async Task RestoreAsync(IDataObject? backup)
    {
        if (backup is null) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Log.Error("[Inject] dispatcher unavailable for Restore");
            return;
        }

        try
        {
            await dispatcher.InvokeAsync(() => Clipboard.SetDataObject(backup, copy: true));
        }
        catch (Exception ex)
        {
            Log.Error($"[Inject] Restore failed: {ex.Message}");
        }
    }
}
