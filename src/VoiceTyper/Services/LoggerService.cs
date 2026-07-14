namespace VoiceTyper.Services;

public sealed class LoggerService
{
    public void LogInfo(string message) => Log.Info($"[App] {message}");

    public void LogWarning(string message) => Log.Warn($"[App] {message}");

    public void LogError(string message) => Log.Error($"[App] {message}");

    public void LogError(Exception ex) =>
        Log.Error($"[App] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
}
