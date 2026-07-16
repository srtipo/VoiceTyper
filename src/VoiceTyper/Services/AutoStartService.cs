using System;
using System.Diagnostics;
using Microsoft.Win32;
using VoiceTyper.Services;

namespace VoiceTyper.Services;

public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceTyper";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var v = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(v);
            }
            catch (Exception ex)
            {
                Log.Warn($"[AutoStart] read failed: {ex.Message}");
                return false;
            }
        }
    }

    public string? CurrentValue
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                Log.Error("[AutoStart] could not open HKCU Run key for write");
                return;
            }

            if (enabled)
            {
                var exePath = GetExecutablePath();
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    Log.Error("[AutoStart] could not resolve executable path");
                    return;
                }
                key.SetValue(ValueName, $"\"{exePath}\" --autostart");
                Log.Info($"[AutoStart] enabled -> {exePath}");
            }
            else
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    Log.Info("[AutoStart] disabled (registry value removed)");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoStart] SetEnabled({enabled}) failed: {ex.Message}");
        }
    }

    public void SyncTo(bool desiredEnabled)
    {
        var current = IsEnabled;
        if (current != desiredEnabled)
        {
            Log.Info($"[AutoStart] drift detected (registry={current}, settings={desiredEnabled}), fixing");
            SetEnabled(desiredEnabled);
        }
    }

    private static string? GetExecutablePath()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && !path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var module = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(module) && !module.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return module;
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoStart] GetExecutablePath failed: {ex.Message}");
            return null;
        }
    }
}
