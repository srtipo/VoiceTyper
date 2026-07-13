using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoiceTyper.Services;

namespace VoiceTyper;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Global\\VoiceTyper_SingleInstance_v1";
    private Mutex? _singleInstanceMutex;
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            })
            .Build();

        _host.Services.GetRequiredService<TrayIconService>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _host?.Dispose(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }

        base.OnExit(e);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var main = _host.Services.GetRequiredService<MainWindow>();
        if (!main.IsVisible)
        {
            main.Show();
        }
        main.WindowState = WindowState.Normal;
        main.Activate();
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
