using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using VoiceTyper.Models;
using VoiceTyper.Services;
using VoiceTyper.ViewModels;

namespace VoiceTyper.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly AutoStartService _autoStart;

    public SettingsWindow(SettingsViewModel vm, SettingsService settings, AutoStartService autoStart)
    {
        InitializeComponent();
        _settings = settings;
        _autoStart = autoStart;
        DataContext = vm;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        try
        {
            var newSettings = vm.BuildSettings();
            _autoStart.SyncTo(newSettings.AutoStart);
            _settings.Save(newSettings);
            Log.Info("[Settings] saved");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error($"[Settings] save failed: {ex.Message}");
            MessageBox.Show($"No se pudieron guardar los cambios:\n{ex.Message}",
                "VoiceTyper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}

public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Collapsed;
    }
}
