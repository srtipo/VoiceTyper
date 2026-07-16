using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VoiceTyper.Models;
using VoiceTyper.Native;
using VoiceTyper.Services;

namespace VoiceTyper.Services;

public sealed class CursorIndicatorService : IDisposable
{
    private const double DotSize = 16.0;
    private const double OffsetX = 20.0;
    private const double OffsetY = 20.0;
    private const int CursorFollowFps = 30;
    private const int AnimationFps = 60;

    private readonly IndicatorWindow _window;
    private readonly DispatcherTimer _followTimer;
    private readonly DispatcherTimer _animTimer;
    private DateTime _animStart;
    private RecordingState _currentState = RecordingState.Idle;
    private bool _disposed;

    public CursorIndicatorService()
    {
        _window = new IndicatorWindow();

        _followTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / CursorFollowFps)
        };
        _followTimer.Tick += (_, _) => UpdatePosition();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / AnimationFps)
        };
        _animTimer.Tick += (_, _) => UpdateAnimation();
    }

    public void Show(RecordingState state)
    {
        if (_disposed) return;
        _currentState = state;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.CheckAccess())
        {
            ApplyState(state);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => ApplyState(state)));
        }
    }

    public void Hide()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.CheckAccess())
        {
            ApplyHide();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(ApplyHide));
        }
    }

    private void ApplyState(RecordingState state)
    {
        if (_disposed) return;

        switch (state)
        {
            case RecordingState.Recording:
                _window.SetFill(Color.FromRgb(0xE5, 0x39, 0x35));
                _animStart = DateTime.UtcNow;
                ShowWindow();
                _animTimer.Start();
                break;

            case RecordingState.Processing:
                _window.SetFill(Color.FromRgb(0xFF, 0xB3, 0x00));
                _animStart = DateTime.UtcNow;
                ShowWindow();
                _animTimer.Start();
                break;

            case RecordingState.Error:
                _window.SetFill(Color.FromRgb(0xB7, 0x1C, 0x1C));
                _animTimer.Stop();
                _window.SetOpacity(1.0);
                ShowWindow();
                break;

            default:
                ApplyHide();
                break;
        }
    }

    private void ApplyHide()
    {
        _followTimer.Stop();
        _animTimer.Stop();
        _window.Hide();
    }

    private void ShowWindow()
    {
        UpdatePosition();
        if (!_window.IsVisible)
        {
            _window.Show();
        }
        _followTimer.Start();
    }

    private void UpdatePosition()
    {
        if (_disposed) return;
        if (!_window.IsVisible) return;

        if (!CursorInterop.TryGetCursorPos(out var physX, out var physY))
        {
            return;
        }

        var workArea = CursorInterop.GetWorkArea(physX, physY);

        var src = PresentationSource.FromVisual(_window);
        double scaleX = 1.0;
        double scaleY = 1.0;
        if (src?.CompositionTarget is { } ct)
        {
            var m = ct.TransformFromDevice;
            scaleX = m.M11;
            scaleY = m.M22;
        }

        var workLeft = workArea.Left * scaleX;
        var workTop = workArea.Top * scaleY;
        var workRight = workArea.Right * scaleX;
        var workBottom = workArea.Bottom * scaleY;

        var cursorDipX = physX * scaleX;
        var cursorDipY = physY * scaleY;

        var x = cursorDipX + OffsetX;
        var y = cursorDipY + OffsetY;

        if (y + DotSize > workBottom)
        {
            y = cursorDipY - DotSize - 4;
        }
        if (x + DotSize > workRight)
        {
            x = cursorDipX - DotSize - 4;
        }
        if (x < workLeft) x = workLeft;
        if (y < workTop) y = workTop;

        _window.Left = x;
        _window.Top = y;
    }

    private void UpdateAnimation()
    {
        if (_disposed) return;

        var elapsed = (DateTime.UtcNow - _animStart).TotalSeconds;
        switch (_currentState)
        {
            case RecordingState.Recording:
                var pulse = 0.5 + 0.5 * Math.Abs(Math.Sin(2.0 * Math.PI * elapsed / 0.9));
                _window.SetOpacity(0.5 + 0.5 * pulse);
                break;

            case RecordingState.Processing:
                var procPulse = 0.5 + 0.5 * Math.Abs(Math.Sin(2.0 * Math.PI * elapsed / 0.6));
                _window.SetOpacity(0.5 + 0.5 * procPulse);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _followTimer.Stop();
        _animTimer.Stop();
        _window.Close();
        _window.Dispatcher.Invoke(() => _window.Content = null);
        Log.Info("[CursorIndicator] disposed");
        GC.SuppressFinalize(this);
    }

    private sealed class IndicatorWindow : Window
    {
        private readonly Ellipse _dot;
        private readonly Grid _root;

        public IndicatorWindow()
        {
            Width = DotSize;
            Height = DotSize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            IsHitTestVisible = false;
            ShowInTaskbar = false;
            Focusable = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000;
            Top = -10000;

            _dot = new Ellipse
            {
                Width = DotSize,
                Height = DotSize,
                Fill = Brushes.Gray
            };

            _root = new Grid { Width = DotSize, Height = DotSize };
            _root.Children.Add(_dot);
            Content = _root;
        }

        public void SetFill(Color c) => _dot.Fill = new SolidColorBrush(c);

        public void SetOpacity(double o) => _dot.Opacity = Math.Clamp(o, 0.0, 1.0);
    }
}
