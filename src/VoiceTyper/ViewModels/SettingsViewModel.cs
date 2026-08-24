using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using VoiceTyper.Models;
using VoiceTyper.Native;
using VoiceTyper.Services;

namespace VoiceTyper.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AutoStartService _autoStart;
    private readonly ModelManagerService _modelManager;
    private readonly HotkeyService _hotkey;
    private readonly CudaDetector _cudaDetector;
    private readonly TranscriptionRouter _transcriber;

    [ObservableProperty]
    private TranscriptionEngine _selectedEngine;

    [ObservableProperty]
    private WhisperModel _selectedModel;

    [ObservableProperty]
    private Wav2Vec2Model _selectedWav2Vec2Model;

    [ObservableProperty]
    private string _selectedLanguage = "es";

    [ObservableProperty]
    private string _selectedHotkeyModifier = "RMenu";

    [ObservableProperty]
    private string _selectedHotkeyTrigger = "Space";

    [ObservableProperty]
    private int _selectedMicrophoneIndex = -1;

    [ObservableProperty]
    private bool _autoStartEnabled;

    [ObservableProperty]
    private bool _pauseOnFullscreen = true;

    [ObservableProperty]
    private bool _restoreClipboard = true;

    [ObservableProperty]
    private bool _gpuEnabled;

    [ObservableProperty]
    private int _gpuDeviceIndex;

    [ObservableProperty]
    private string _backendMode = "CPU";

    [ObservableProperty]
    private string _testHotkeyFeedback = "Sin probar";

    [ObservableProperty]
    private string _testHotkeyFeedbackColor = "#455A64";

    [ObservableProperty]
    private bool _isTestingHotkey;

    [ObservableProperty]
    private bool _isModelDownloading;

    [ObservableProperty]
    private double _downloadPercent;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    public ObservableCollection<WhisperModel> ModelOptions { get; } = new()
    {
        WhisperModel.Tiny,
        WhisperModel.Base,
        WhisperModel.Small,
        WhisperModel.Medium,
        WhisperModel.LargeV3Turbo
    };

    public ObservableCollection<Wav2Vec2Model> Wav2Vec2ModelOptions { get; } = new()
    {
        Wav2Vec2Model.SpanishXlsr53
    };

    public ObservableCollection<EngineOption> EngineOptions { get; } = new()
    {
        new EngineOption(TranscriptionEngine.Whisper, "Whisper (OpenAI)"),
        new EngineOption(TranscriptionEngine.Wav2Vec2, "Wav2Vec2 (MMS, Meta)")
    };

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new()
    {
        new LanguageOption("es", "Español"),
        new LanguageOption("en", "English"),
        new LanguageOption("pt", "Português"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("auto", "Auto-detect")
    };

    public ObservableCollection<HotkeyOption> ModifierOptions { get; } = new()
    {
        new HotkeyOption("RMenu", "Alt Gr (Right Alt)"),
        new HotkeyOption("LAlt", "Left Alt"),
        new HotkeyOption("LCtrl", "Left Ctrl"),
        new HotkeyOption("RCtrl", "Right Ctrl"),
        new HotkeyOption("LShift", "Left Shift"),
        new HotkeyOption("RShift", "Right Shift")
    };

    public ObservableCollection<HotkeyOption> TriggerOptions { get; } = new()
    {
        new HotkeyOption("Space", "Space"),
        new HotkeyOption("Enter", "Enter"),
        new HotkeyOption("F1", "F1"),
        new HotkeyOption("F2", "F2"),
        new HotkeyOption("F3", "F3"),
        new HotkeyOption("F4", "F4"),
        new HotkeyOption("F5", "F5"),
        new HotkeyOption("F6", "F6"),
        new HotkeyOption("F7", "F7"),
        new HotkeyOption("F8", "F8"),
        new HotkeyOption("F9", "F9"),
        new HotkeyOption("F10", "F10"),
        new HotkeyOption("F11", "F11"),
        new HotkeyOption("F12", "F12")
    };

    public ObservableCollection<MicrophoneOption> MicrophoneOptions { get; } = new()
    {
        new MicrophoneOption(-1, "Default (sistema)")
    };

    public ObservableCollection<CudaDevice> GpuDeviceOptions { get; } = new();

    public bool IsModelDownloaded => SelectedEngine == TranscriptionEngine.Wav2Vec2
        ? _modelManager.IsWav2Vec2ModelAvailable(SelectedWav2Vec2Model)
        : _modelManager.IsModelAvailable(SelectedModel);

    public bool IsLargeModelSelected =>
        SelectedEngine == TranscriptionEngine.Whisper
        && (SelectedModel == WhisperModel.Medium || SelectedModel == WhisperModel.LargeV3Turbo);

    public bool IsWhisperEngineSelected => SelectedEngine == TranscriptionEngine.Whisper;

    public bool IsWav2Vec2EngineSelected => SelectedEngine == TranscriptionEngine.Wav2Vec2;

    public bool HasGpuOptions => GpuDeviceOptions.Count > 0;

    public SettingsViewModel(
        SettingsService settings,
        AutoStartService autoStart,
        ModelManagerService modelManager,
        HotkeyService hotkey,
        CudaDetector cudaDetector,
        TranscriptionRouter transcriber)
    {
        _settings = settings;
        _autoStart = autoStart;
        _modelManager = modelManager;
        _hotkey = hotkey;
        _cudaDetector = cudaDetector;
        _transcriber = transcriber;

        var current = settings.Current;
        _selectedEngine = current.Engine;
        _selectedModel = current.Model;
        _selectedWav2Vec2Model = current.Wav2Vec2Model;
        _selectedLanguage = current.Language;
        _selectedHotkeyModifier = current.HotkeyModifier;
        _selectedHotkeyTrigger = current.HotkeyTrigger;
        _selectedMicrophoneIndex = current.MicrophoneDeviceIndex;
        _autoStartEnabled = current.AutoStart;
        _pauseOnFullscreen = current.PauseOnFullscreen;
        _restoreClipboard = current.RestoreClipboard;
        _gpuEnabled = current.GpuEnabled;
        _gpuDeviceIndex = current.GpuDeviceIndex;
        _backendMode = _transcriber.BackendMode;

        LoadMicrophoneDevices();
        LoadGpuDevices();
    }

    private void LoadMicrophoneDevices()
    {
        try
        {
            var count = WaveIn.DeviceCount;
            for (var i = 0; i < count; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                MicrophoneOptions.Add(new MicrophoneOption(i, caps.ProductName));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[SettingsViewModel] failed to enumerate microphones: {ex.Message}");
        }
    }

    private void LoadGpuDevices()
    {
        try
        {
            foreach (var device in _cudaDetector.GetDevices())
            {
                GpuDeviceOptions.Add(device);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[SettingsViewModel] failed to enumerate GPUs: {ex.Message}");
        }
    }

    partial void OnSelectedModelChanged(WhisperModel value)
    {
        OnPropertyChanged(nameof(IsModelDownloaded));
        OnPropertyChanged(nameof(IsLargeModelSelected));
    }

    partial void OnSelectedWav2Vec2ModelChanged(Wav2Vec2Model value)
    {
        OnPropertyChanged(nameof(IsModelDownloaded));
    }

    partial void OnSelectedEngineChanged(TranscriptionEngine value)
    {
        OnPropertyChanged(nameof(IsModelDownloaded));
        OnPropertyChanged(nameof(IsLargeModelSelected));
        OnPropertyChanged(nameof(IsWhisperEngineSelected));
        OnPropertyChanged(nameof(IsWav2Vec2EngineSelected));
    }

    public AppSettings BuildSettings()
    {
        return new AppSettings
        {
            Engine = SelectedEngine,
            Model = SelectedModel,
            Wav2Vec2Model = SelectedWav2Vec2Model,
            Language = SelectedLanguage,
            HotkeyModifier = SelectedHotkeyModifier,
            HotkeyTrigger = SelectedHotkeyTrigger,
            MicrophoneDeviceIndex = SelectedMicrophoneIndex,
            AutoStart = AutoStartEnabled,
            PauseOnFullscreen = PauseOnFullscreen,
            RestoreClipboard = RestoreClipboard,
            GpuEnabled = GpuEnabled,
            GpuDeviceIndex = GpuDeviceIndex
        };
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task TestHotkeyAsync(System.Threading.CancellationToken token)
    {
        if (IsTestingHotkey) return;

        IsTestingHotkey = true;
        TestHotkeyFeedback = $"Mantener {FormatKey(SelectedHotkeyModifier)} + {FormatKey(SelectedHotkeyTrigger)} (3 s)…";
        TestHotkeyFeedbackColor = "#1976D2";

        try
        {
            var detected = await _hotkey.TestAsync(3000, token);
            if (detected)
            {
                TestHotkeyFeedback = "✓ Detectado";
                TestHotkeyFeedbackColor = "#2E7D32";
            }
            else
            {
                TestHotkeyFeedback = "✗ No detectado (timeout)";
                TestHotkeyFeedbackColor = "#C62828";
            }
        }
        catch (Exception ex)
        {
            TestHotkeyFeedback = $"Error: {ex.Message}";
            TestHotkeyFeedbackColor = "#C62828";
            Log.Error($"[SettingsViewModel] TestHotkey failed: {ex.Message}");
        }
        finally
        {
            IsTestingHotkey = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DownloadModelAsync(System.Threading.CancellationToken token)
    {
        if (IsModelDownloading) return;

        IsModelDownloading = true;
        DownloadPercent = 0;
        DownloadStatus = "Conectando con HuggingFace…";

        var progress = new Progress<double>(pct =>
        {
            DownloadPercent = pct;
            DownloadStatus = $"Descargado: {pct:F0}%";
        });

        try
        {
            await _modelManager.EnsureActiveModelAsync(SelectedEngine, SelectedModel, SelectedWav2Vec2Model, progress, token);
            DownloadStatus = "Modelo descargado";
            DownloadPercent = 100;
            OnPropertyChanged(nameof(IsModelDownloaded));
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Descarga cancelada";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
            Log.Error($"[SettingsViewModel] download failed: {ex.Message}");
        }
        finally
        {
            IsModelDownloading = false;
        }
    }

    private static string FormatKey(string name)
    {
        return name switch
        {
            "RMenu" => "AltGr",
            "LAlt" => "Left Alt",
            "LShift" => "Left Shift",
            "RShift" => "Right Shift",
            "LCtrl" => "Left Ctrl",
            "RCtrl" => "Right Ctrl",
            "Space" => "Space",
            "Enter" => "Enter",
            _ => name
        };
    }
}

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record HotkeyOption(string Name, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record MicrophoneOption(int Index, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record EngineOption(TranscriptionEngine Engine, string DisplayName)
{
    public override string ToString() => DisplayName;
}
