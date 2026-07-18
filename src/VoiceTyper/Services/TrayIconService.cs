using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly IServiceProvider _services;
    private TranscriberService? _transcriber;
    private RecordingState _state = RecordingState.Idle;
    private MenuItem? _statusItem;
    private MenuItem? _modelMenu;
    private MenuItem? _languageMenu;
    private MenuItem? _autoStartItem;
    private MenuItem? _pauseOnFullscreenItem;
    private MenuItem? _retryDownloadItem;

    public event Action? OpenSettingsRequested;
    public event Action? RetryDownloadRequested;
    public event Action? AboutRequested;
    public event Action? ExitRequested;
    public event Action<WhisperModel>? ModelChangeRequested;
    public event Action<string>? LanguageChangeRequested;
    public event Action<bool>? AutoStartToggleRequested;
    public event Action<bool>? PauseOnFullscreenToggleRequested;

    public TrayIconService(IServiceProvider services)
    {
        _services = services;
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "VoiceTyper — Inactivo",
            Icon = LoadIcon("tray-idle"),
            Visibility = Visibility.Visible
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
    }

    private string GetBackendMode()
    {
        if (_transcriber is null)
        {
            try
            {
                _transcriber = _services.GetService(typeof(TranscriberService)) as TranscriberService;
            }
            catch
            {
            }
        }
        return _transcriber?.BackendMode ?? "CPU";
    }

    public void SetState(RecordingState state)
    {
        _state = state;
        _trayIcon.Icon = state switch
        {
            RecordingState.Idle => LoadIcon("tray-idle"),
            RecordingState.Recording => LoadIcon("tray-recording"),
            RecordingState.Processing => LoadIcon("tray-processing"),
            RecordingState.Error => LoadIcon("tray-error"),
            RecordingState.NotReady => LoadIcon("tray-error"),
            _ => LoadIcon("tray-idle")
        };
        _trayIcon.ToolTipText = state switch
        {
            RecordingState.Idle => $"VoiceTyper — Inactivo ({GetBackendMode()})",
            RecordingState.Recording => "VoiceTyper — Grabando…",
            RecordingState.Processing => $"VoiceTyper — Procesando… ({GetBackendMode()})",
            RecordingState.Error => "VoiceTyper — Error",
            RecordingState.NotReady => "VoiceTyper — Modelo no descargado",
            _ => "VoiceTyper"
        };

        if (_statusItem is not null)
        {
            _statusItem.Header = GetStatusHeader(state);
        }
    }

    public void BuildContextMenu(AppSettings settings, bool modelAvailable)
    {
        var menu = new ContextMenu();

        _statusItem = new MenuItem
        {
            Header = GetStatusHeader(_state),
            IsEnabled = false
        };
        menu.Items.Add(_statusItem);

        menu.Items.Add(new Separator());

        var openSettingsItem = new MenuItem { Header = "Configuración…" };
        openSettingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke();
        menu.Items.Add(openSettingsItem);

        _retryDownloadItem = new MenuItem { Header = "Reintentar descarga" };
        _retryDownloadItem.Click += (_, _) => RetryDownloadRequested?.Invoke();
        _retryDownloadItem.Visibility = modelAvailable ? Visibility.Collapsed : Visibility.Visible;
        menu.Items.Add(_retryDownloadItem);

        menu.Items.Add(new Separator());

        _modelMenu = BuildCheckableSubmenu(
            "Modelo",
            new Dictionary<WhisperModel, string>
            {
                { WhisperModel.Base, "Base" },
                { WhisperModel.Small, "Small" },
                { WhisperModel.Medium, "Medium" }
            },
            settings.Model,
            m => ModelChangeRequested?.Invoke(m));
        menu.Items.Add(_modelMenu);

        _languageMenu = BuildCheckableSubmenu(
            "Idioma",
            new Dictionary<string, string>
            {
                { "es", "Español" },
                { "en", "English" },
                { "pt", "Português" },
                { "fr", "Français" },
                { "auto", "Auto" }
            },
            settings.Language,
            l => LanguageChangeRequested?.Invoke(l));
        menu.Items.Add(_languageMenu);

        menu.Items.Add(new Separator());

        _autoStartItem = new MenuItem
        {
            Header = "Iniciar con Windows",
            IsCheckable = true,
            IsChecked = settings.AutoStart
        };
        _autoStartItem.Click += (_, _) => AutoStartToggleRequested?.Invoke(_autoStartItem.IsChecked);
        menu.Items.Add(_autoStartItem);

        _pauseOnFullscreenItem = new MenuItem
        {
            Header = "Pausar en pantalla completa",
            IsCheckable = true,
            IsChecked = settings.PauseOnFullscreen
        };
        _pauseOnFullscreenItem.Click += (_, _) => PauseOnFullscreenToggleRequested?.Invoke(_pauseOnFullscreenItem.IsChecked);
        menu.Items.Add(_pauseOnFullscreenItem);

        menu.Items.Add(new Separator());

        var aboutItem = new MenuItem { Header = "Acerca de…" };
        aboutItem.Click += (_, _) => AboutRequested?.Invoke();
        menu.Items.Add(aboutItem);

        var exitItem = new MenuItem { Header = "Salir" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
    }

    public void UpdateModelInMenu(WhisperModel model)
    {
        UpdateCheckableSubmenu(_modelMenu, model.ToString(), m => m == model.ToString(),
            () => _modelMenu?.Items);
    }

    public void UpdateLanguageInMenu(string language)
    {
        UpdateCheckableSubmenu(_languageMenu, language, l => l == language,
            () => _languageMenu?.Items);
    }

    public void UpdateAutoStartInMenu(bool enabled)
    {
        if (_autoStartItem is not null) _autoStartItem.IsChecked = enabled;
    }

    public void UpdatePauseOnFullscreenInMenu(bool enabled)
    {
        if (_pauseOnFullscreenItem is not null) _pauseOnFullscreenItem.IsChecked = enabled;
    }

    public void UpdateRetryDownloadVisibility(bool modelAvailable)
    {
        if (_retryDownloadItem is not null)
        {
            _retryDownloadItem.Visibility = modelAvailable ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public void ShowBalloon(string title, string message)
    {
        _trayIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    private static string GetStatusHeader(RecordingState state) => state switch
    {
        RecordingState.Idle => "⚪ Listo",
        RecordingState.Recording => "🔴 Grabando",
        RecordingState.Processing => "🟡 Procesando",
        RecordingState.Error => "⚠ Error",
        RecordingState.NotReady => "⚠ Modelo no descargado",
        _ => "VoiceTyper"
    };

    private static MenuItem BuildCheckableSubmenu<T>(
        string header,
        Dictionary<T, string> options,
        T currentValue,
        Action<T> onSelect) where T : notnull
    {
        var top = new MenuItem { Header = header };
        foreach (var (key, label) in options)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = EqualityComparer<T>.Default.Equals(key, currentValue),
                Tag = key
            };
            item.Click += (_, _) =>
            {
                onSelect(key);
                foreach (MenuItem sibling in top.Items)
                {
                    if (sibling is MenuItem mi) mi.IsChecked = false;
                }
                item.IsChecked = true;
            };
            top.Items.Add(item);
        }
        return top;
    }

    private void UpdateCheckableSubmenu(
        MenuItem? top,
        string newValue,
        Func<string, bool> matches,
        Func<ItemCollection?> getItems)
    {
        if (top is null) return;
        var items = top.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is MenuItem mi)
            {
                var key = mi.Tag?.ToString() ?? string.Empty;
                mi.IsChecked = matches(key);
            }
        }
    }

    private static System.Drawing.Icon LoadIcon(string name)
    {
        var uri = new Uri($"pack://application:,,,/resources/{name}.ico", UriKind.Absolute);
        var streamInfo = Application.GetResourceStream(uri);
        if (streamInfo?.Stream is null)
        {
            var uri2 = new Uri($"pack://application:,,,/Resources/{name}.ico", UriKind.Absolute);
            streamInfo = Application.GetResourceStream(uri2);
        }
        if (streamInfo?.Stream is null)
            throw new InvalidOperationException($"No se pudo cargar el icono {name}.ico");
        return new System.Drawing.Icon(streamInfo.Stream);
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}
