using System;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceTyper.Services;

namespace VoiceTyper.Views;

[ObservableObject]
public partial class ModelDownloadWindow : Window
{
    [ObservableProperty]
    private double _downloadPercent;

    [ObservableProperty]
    private string _downloadStatus = "Iniciando descarga...";

    public event Action? CancelRequested;

    public ModelDownloadWindow()
    {
        InitializeComponent();
        DataContext = this;
        CancelButton.Click += (_, _) => CancelRequested?.Invoke();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Log.Info("[Download] window closed by user (download continues in background if not cancelled)");
        base.OnClosing(e);
    }
}
