using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VoiceTyper.Services;

/// <summary>
/// Carga variables de entorno desde un archivo .env junto al ejecutable.
/// Precedencia: variable del sistema > .env > defaults del código.
/// Thread-safe. Cargar una sola vez en App.OnStartup antes del Host.
/// </summary>
public static class Env
{
    private const string EnvFileName = ".env";
    private static readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();
    private static bool _loaded;

    public static string Language { get; private set; } = "es";
    public static string Model { get; private set; } = "small";
    public static string LogLevel { get; private set; } = "Information";
    public static string? ModelDir { get; private set; }
    public static string? LogDir { get; private set; }
    public static string? WorkDir { get; private set; }
    public static string OpenAIApiKey { get; private set; } = "";
    public static string GroqApiKey { get; private set; } = "";

    public static IReadOnlyDictionary<string, string> All
    {
        get
        {
            lock (_lock) { return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase); }
        }
    }

    public static void Load()
    {
        lock (_lock)
        {
            if (_loaded) return;

            var envPath = ResolveEnvPath();
            if (envPath is not null && File.Exists(envPath))
            {
                foreach (var (key, value) in ParseFile(envPath))
                {
                    _values[key] = value;
                }
            }

            ApplyToProperties();
            _loaded = true;
        }
    }

    public static string? Get(string key)
    {
        lock (_lock)
        {
            if (_values.TryGetValue(key, out var v)) return v;
        }
        return Environment.GetEnvironmentVariable(key);
    }

    private static string? ResolveEnvPath()
    {
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (exeDir is not null) return Path.Combine(exeDir, EnvFileName);

        var cwd = Directory.GetCurrentDirectory();
        return Path.Combine(cwd, EnvFileName);
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseFile(string path)
    {
        var secrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_KEY", "_SECRET", "_TOKEN", "_PASSWORD" };
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (secrets.Any(suffix => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrEmpty(value))
                {
                    System.Diagnostics.Debug.WriteLine($"[Env] {key}=<redacted>");
                }
                continue;
            }

            if (key.Length > 0 && value.Length > 0)
            {
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
    }

    private static void ApplyToProperties()
    {
        Language = GetOrDefault("VT_LANGUAGE", Language);
        Model = GetOrDefault("VT_MODEL", Model);
        LogLevel = GetOrDefault("VT_LOG_LEVEL", LogLevel);
        ModelDir = EmptyToNull(Get("VT_MODEL_DIR"));
        LogDir = EmptyToNull(Get("VT_LOG_DIR"));
        WorkDir = EmptyToNull(Get("VT_WORK_DIR"));
        OpenAIApiKey = GetOrDefault("VT_OPENAI_API_KEY", OpenAIApiKey);
        GroqApiKey = GetOrDefault("VT_GROQ_API_KEY", GroqApiKey);
    }

    private static string GetOrDefault(string key, string fallback)
    {
        var v = Get(key);
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }

    private static string? EmptyToNull(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
}
