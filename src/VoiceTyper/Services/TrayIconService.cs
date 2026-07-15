using System;
using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly IServiceProvider _services;
    private RecordingState _state = RecordingState.Idle;

    public TrayIconService(IServiceProvider services)
    {
        _services = services;
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "VoiceTyper — Inactivo (mantener AltGr+Space para grabar)",
            Icon = LoadIcon("tray-idle"),
            ContextMenu = (System.Windows.Controls.ContextMenu)Application.Current.FindResource("TrayContextMenu"),
            Visibility = Visibility.Visible
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) => OpenMainWindow();
    }

    private void OpenMainWindow()
    {
        var main = _services.GetRequiredService<MainWindow>();
        if (!main.IsVisible)
        {
            main.Show();
        }
        main.WindowState = WindowState.Normal;
        main.Activate();
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
            RecordingState.Idle => "VoiceTyper — Inactivo (mantener AltGr+Space para grabar)",
            RecordingState.Recording => "VoiceTyper — Grabando…",
            RecordingState.Processing => "VoiceTyper — Procesando…",
            RecordingState.Error => "VoiceTyper — Error",
            RecordingState.NotReady => "VoiceTyper — Modelo no descargado",
            _ => "VoiceTyper"
        };
    }

    public void ShowBalloon(string title, string message)
    {
        _trayIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
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
