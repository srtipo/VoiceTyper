using System;
using System.IO;
using System.Text;

namespace VoiceTyper.Services;

public static class Log
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static bool _initialized;

    public static string LogPath
    {
        get
        {
            EnsureInitialized();
            return _logPath!;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            EnsureInitialized();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetBytes(line);
            lock (_lock)
            {
                using var stream = new FileStream(
                    _logPath!,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }
        catch
        {
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiceTyper",
                "logs");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "voicetyper.log");
            _initialized = true;
        }
    }
}
