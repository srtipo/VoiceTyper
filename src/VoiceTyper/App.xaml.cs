using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using VoiceTyper.Models;
using VoiceTyper.Native;
using VoiceTyper.Services;
using VoiceTyper.ViewModels;
using VoiceTyper.Views;

namespace VoiceTyper;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Global\\VoiceTyper_SingleInstance_v1";
    private Mutex? _singleInstanceMutex;
    private IHost? _host;

    private CancellationTokenSource? _downloadCts;
    private readonly object _downloadLock = new();
    private ModelDownloadWindow? _downloadWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Env.Load();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddSingleton<LowLevelKeyboardHook>();
                services.AddSingleton<HotkeyService>();
                services.AddSingleton<AudioRecorderService>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<LoggerService>();
                services.AddSingleton<AutoStartService>();
                services.AddSingleton<CursorIndicatorService>();
                services.AddHttpClient<ModelManagerService>(c => c.Timeout = TimeSpan.FromMinutes(10));
                services.AddSingleton<TranscriberService>();
                services.AddSingleton<TextInjectorService>();
                services.AddSingleton<RecordingOrchestrator>();
            })
            .Build();

        _host.Services.GetRequiredService<TrayIconService>();
        _host.Services.GetRequiredService<SettingsService>();
        _host.Services.GetRequiredService<LoggerService>();
        _host.Services.GetRequiredService<TranscriberService>();
        _host.Services.GetRequiredService<TextInjectorService>();
        _host.Services.GetRequiredService<AutoStartService>();
        _host.Services.GetRequiredService<CursorIndicatorService>();
        _host.Services.GetRequiredService<HotkeyService>();
        _host.Services.GetRequiredService<RecordingOrchestrator>();

        WireTrayEvents();
        ApplyStartupSettings();

        _ = EnsureModelAndStartAsync();
    }

    private void ApplyStartupSettings()
    {
        if (_host is null) return;
        var settings = _host.Services.GetRequiredService<SettingsService>();
        var autoStart = _host.Services.GetRequiredService<AutoStartService>();
        var hotkey = _host.Services.GetRequiredService<HotkeyService>();
        var tray = _host.Services.GetRequiredService<TrayIconService>();
        var modelMgr = _host.Services.GetRequiredService<ModelManagerService>();

        hotkey.ApplySettings(settings.Current);
        autoStart.SyncTo(settings.Current.AutoStart);
        tray.BuildContextMenu(settings.Current, modelMgr.IsModelAvailable(settings.Current.Model));

        settings.Changed += s =>
        {
            hotkey.ApplySettings(s);
            autoStart.SyncTo(s.AutoStart);
            tray.UpdateAutoStartInMenu(s.AutoStart);
            tray.UpdatePauseOnFullscreenInMenu(s.PauseOnFullscreen);
            tray.UpdateModelInMenu(s.Model);
            tray.UpdateLanguageInMenu(s.Language);
            tray.UpdateRetryDownloadVisibility(modelMgr.IsModelAvailable(s.Model));
        };
    }

    private void WireTrayEvents()
    {
        if (_host is null) return;
        var tray = _host.Services.GetRequiredService<TrayIconService>();
        var settings = _host.Services.GetRequiredService<SettingsService>();
        var modelMgr = _host.Services.GetRequiredService<ModelManagerService>();
        var autoStart = _host.Services.GetRequiredService<AutoStartService>();
        var hotkey = _host.Services.GetRequiredService<HotkeyService>();

        tray.OpenSettingsRequested += () => OnOpenSettings(this, new RoutedEventArgs());
        tray.RetryDownloadRequested += () => OnRetryDownload(this, new RoutedEventArgs());
        tray.AboutRequested += () => OnAbout(this, new RoutedEventArgs());
        tray.ExitRequested += () => OnExit(this, new RoutedEventArgs());

        tray.ModelChangeRequested += m =>
        {
            var updated = settings.Current;
            updated.Model = m;
            settings.Save(updated);
            if (!modelMgr.IsModelAvailable(m))
            {
                _ = EnsureModelAndStartAsync();
            }
        };

        tray.LanguageChangeRequested += l =>
        {
            var updated = settings.Current;
            updated.Language = l;
            settings.Save(updated);
        };

        tray.AutoStartToggleRequested += enabled =>
        {
            var updated = settings.Current;
            updated.AutoStart = enabled;
            autoStart.SyncTo(enabled);
            settings.Save(updated);
        };

        tray.PauseOnFullscreenToggleRequested += enabled =>
        {
            var updated = settings.Current;
            updated.PauseOnFullscreen = enabled;
            settings.Save(updated);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _host?.Dispose(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }

        base.OnExit(e);
    }

    private static bool IsSilentMode()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var a in args)
        {
            if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private async Task EnsureModelAndStartAsync()
    {
        var tray = _host!.Services.GetRequiredService<TrayIconService>();
        var settings = _host.Services.GetRequiredService<SettingsService>();
        var models = _host.Services.GetRequiredService<ModelManagerService>();
        var hotkey = _host.Services.GetRequiredService<HotkeyService>();

        var model = settings.Current.Model;
        var silent = IsSilentMode();

        Dispatcher.Invoke(() => tray.SetState(RecordingState.NotReady));

        if (models.IsModelAvailable(model))
        {
            Log.Info($"[Startup] model {model} already on disk, skipping download");
            hotkey.IsReady = true;
            hotkey.Start();
            tray.UpdateRetryDownloadVisibility(true);
            Dispatcher.Invoke(() => tray.SetState(RecordingState.Idle));
            return;
        }

        Log.Info($"[Startup] model {model} not on disk, downloading (silent={silent})");

        CancellationTokenSource cts;
        lock (_downloadLock)
        {
            _downloadCts = new CancellationTokenSource();
            cts = _downloadCts;
        }

        var progress = new Progress<double>(pct =>
        {
            if (_downloadWindow is not null)
            {
                _downloadWindow.DownloadPercent = pct;
                _downloadWindow.DownloadStatus = $"Descargado: {pct:F0}%";
            }
        });

        if (!silent)
        {
            Dispatcher.Invoke(() =>
            {
                _downloadWindow = new ModelDownloadWindow();
                _downloadWindow.DownloadStatus = "Conectando con HuggingFace...";
                _downloadWindow.DownloadPercent = 0;
                _downloadWindow.CancelRequested += () =>
                {
                    Log.Info("[Startup] download cancel requested by user");
                    cts.Cancel();
                };
                _downloadWindow.Show();
            });
        }
        else
        {
            Log.Info("[Startup] silent mode, no download window shown");
        }

        try
        {
            await models.EnsureModelAsync(model, progress, cts.Token).ConfigureAwait(false);
            Log.Info($"[Startup] model {model} downloaded successfully");

            Dispatcher.Invoke(() => _downloadWindow?.Close());
            _downloadWindow = null;

            hotkey.IsReady = true;
            hotkey.Start();
            tray.UpdateRetryDownloadVisibility(true);
            Dispatcher.Invoke(() => tray.SetState(RecordingState.Idle));
        }
        catch (OperationCanceledException)
        {
            Log.Info("[Startup] model download cancelled by user");
            Dispatcher.Invoke(() => _downloadWindow?.Close());
            _downloadWindow = null;
            Dispatcher.Invoke(() => tray.SetState(RecordingState.NotReady));
        }
        catch (Exception ex)
        {
            Log.Error($"[Startup] model download failed: {ex.Message}");
            Dispatcher.Invoke(() => _downloadWindow?.Close());
            _downloadWindow = null;
            Dispatcher.Invoke(() => tray.SetState(RecordingState.NotReady));
            Dispatcher.Invoke(() => tray.ShowBalloon("VoiceTyper — Error",
                $"No se pudo descargar el modelo. Click derecho en el ícono → 'Reintentar descarga'."));
        }
        finally
        {
            lock (_downloadLock)
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        try
        {
            var window = _host.Services.GetRequiredService<SettingsWindow>();
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error($"[App] OnOpenSettings failed: {ex.Message}");
        }
    }

    private void OnRetryDownload(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        bool alreadyDownloading;
        lock (_downloadLock)
        {
            alreadyDownloading = _downloadCts is not null;
        }

        if (alreadyDownloading)
        {
            Log.Info("[Tray] Reintentar descarga clicked but already in progress, ignoring");
            return;
        }

        var settings = _host.Services.GetRequiredService<SettingsService>();
        var models = _host.Services.GetRequiredService<ModelManagerService>();
        if (models.IsModelAvailable(settings.Current.Model))
        {
            var tray = _host.Services.GetRequiredService<TrayIconService>();
            var hotkey = _host.Services.GetRequiredService<HotkeyService>();
            if (!hotkey.IsReady)
            {
                hotkey.IsReady = true;
                hotkey.Start();
            }
            tray.SetState(RecordingState.Idle);
            tray.UpdateRetryDownloadVisibility(true);
            Log.Info("[Tray] Reintentar descarga: model already present, hook started");
            return;
        }

        Log.Info("[Tray] Reintentar descarga clicked, restarting download");
        _ = EnsureModelAndStartAsync();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "VoiceTyper v0.1.0\n\nDictado por voz global para Windows.\n\nTranscripción local con Whisper.",
            "Acerca de VoiceTyper",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Shutdown(0);
    }
}
