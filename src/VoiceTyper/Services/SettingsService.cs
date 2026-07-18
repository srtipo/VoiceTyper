using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    public SettingsService()
    {
        Current = BuildEffective();
    }

    public void Save(AppSettings settings)
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(settings, _writeOptions);
        File.WriteAllText(path, json);
        Current = settings;
        try
        {
            Changed?.Invoke(settings);
        }
        catch (Exception ex)
        {
            Log.Error($"[Settings] Changed handler threw: {ex.Message}");
        }
    }

    public void Reload()
    {
        Current = BuildEffective();
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceTyper",
            "settings.json");
    }

    private static AppSettings BuildEffective()
    {
        var settings = new AppSettings();
        var path = GetSettingsPath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var fromFile = JsonSerializer.Deserialize<AppSettings>(json, _readOptions);
                if (fromFile is not null)
                {
                    settings = fromFile;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Settings] failed to parse {path}, using defaults: {ex.Message}");
            }
        }

        ApplyEnvOverrides(settings);
        return settings;
    }

    private static void ApplyEnvOverrides(AppSettings s)
    {
        var modelEnv = Env.Get("VT_MODEL");
        if (!string.IsNullOrWhiteSpace(modelEnv)
            && Enum.TryParse<WhisperModel>(modelEnv, ignoreCase: true, out var m))
        {
            s.Model = m;
        }

        var langEnv = Env.Get("VT_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(langEnv))
        {
            s.Language = langEnv;
        }

        var modEnv = Env.Get("VT_HOTKEY_MODIFIER");
        if (!string.IsNullOrWhiteSpace(modEnv))
        {
            s.HotkeyModifier = modEnv;
        }

        var triggerEnv = Env.Get("VT_HOTKEY_TRIGGER");
        if (!string.IsNullOrWhiteSpace(triggerEnv))
        {
            s.HotkeyTrigger = triggerEnv;
        }

        var autoEnv = Env.Get("VT_AUTOSTART");
        if (!string.IsNullOrWhiteSpace(autoEnv)
            && bool.TryParse(autoEnv, out var auto))
        {
            s.AutoStart = auto;
        }

        var pauseEnv = Env.Get("VT_PAUSE_FULLSCREEN");
        if (!string.IsNullOrWhiteSpace(pauseEnv)
            && bool.TryParse(pauseEnv, out var pause))
        {
            s.PauseOnFullscreen = pause;
        }

        var micEnv = Env.Get("VT_MIC_DEVICE");
        if (!string.IsNullOrWhiteSpace(micEnv)
            && int.TryParse(micEnv, out var mic))
        {
            s.MicrophoneDeviceIndex = mic;
        }

        var gpuEnv = Env.Get("VT_GPU_ENABLED");
        if (!string.IsNullOrWhiteSpace(gpuEnv)
            && bool.TryParse(gpuEnv, out var gpu))
        {
            s.GpuEnabled = gpu;
        }

        var gpuDevEnv = Env.Get("VT_GPU_DEVICE");
        if (!string.IsNullOrWhiteSpace(gpuDevEnv)
            && int.TryParse(gpuDevEnv, out var gpuDev))
        {
            s.GpuDeviceIndex = gpuDev;
        }
    }
}
