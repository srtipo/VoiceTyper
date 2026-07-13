using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoiceTyper.Native;

namespace VoiceTyper.Services;

public sealed class HotkeyService : IDisposable
{
    private const int DWMWA_FULLSCREEN = 9;
    private const int ConsumePollMs = 20;
    private const int FullscreenPollMs = 500;

    private readonly LowLevelKeyboardHook _hook;
    private CancellationTokenSource? _loopsCts;
    private TaskCompletionSource<bool>? _currentTcs;
    private bool _disposed;

    public VirtualKey Modifier { get; } = VirtualKey.RMenu;
    public VirtualKey Trigger { get; } = VirtualKey.Space;
    public bool IsRecording { get; private set; }
    public Task RecordingTask => _currentTcs?.Task ?? Task.CompletedTask;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public HotkeyService(LowLevelKeyboardHook hook)
    {
        _hook = hook;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
        if (_loopsCts is not null) return;

        _hook.KeyDown += OnHookKeyDown;
        _hook.KeyUp += OnHookKeyUp;
        _hook.Install();

        _loopsCts = new CancellationTokenSource();
        _ = ConsumeLoopAsync(_loopsCts.Token);
        _ = FullscreenLoopAsync(_loopsCts.Token);
    }

    public void Stop()
    {
        if (_loopsCts is null) return;

        _hook.KeyDown -= OnHookKeyDown;
        _hook.KeyUp -= OnHookKeyUp;
        _hook.Uninstall();

        _loopsCts.Cancel();
        _loopsCts.Dispose();
        _loopsCts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _hook.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnHookKeyDown(VirtualKey vk)
    {
        if (vk != Trigger) return;

        if (IsRecording)
        {
            Debug.WriteLine("[Hotkey] autorepeat ignored");
            Console.WriteLine("[Hotkey] autorepeat ignored");
            return;
        }

        if (!LowLevelKeyboardHook.IsKeyPressed((ushort)Modifier)) return;

        IsRecording = true;
        _currentTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hook.ConsumeNextKeyDown = true;
        Debug.WriteLine("[Hotkey] KeyDown Space, recording=true");
        Console.WriteLine("[Hotkey] KeyDown Space, recording=true");
        RecordingStarted?.Invoke();
    }

    private void OnHookKeyUp(VirtualKey vk)
    {
        if (vk != Trigger) return;
        if (!IsRecording) return;

        IsRecording = false;
        RecordingStopped?.Invoke();
        _currentTcs?.TrySetResult(true);
        _currentTcs = null;
        Debug.WriteLine("[Hotkey] KeyUp Space, recording=false");
        Console.WriteLine("[Hotkey] KeyUp Space, recording=false");
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (IsRecording)
                {
                    _hook.ConsumeNextKeyDown = true;
                }
                await Task.Delay(ConsumePollMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task FullscreenLoopAsync(CancellationToken ct)
    {
        var wasFullscreen = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var isFullscreen = IsForegroundFullscreen();
                if (isFullscreen != wasFullscreen)
                {
                    _hook.IsPaused = isFullscreen;
                    if (isFullscreen)
                    {
                        Debug.WriteLine("[Hotkey] paused");
                        Console.WriteLine("[Hotkey] paused");
                    }
                    else
                    {
                        Debug.WriteLine("[Hotkey] resumed");
                        Console.WriteLine("[Hotkey] resumed");
                    }
                    wasFullscreen = isFullscreen;
                }
                await Task.Delay(FullscreenPollMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool IsForegroundFullscreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var hr = DwmGetWindowAttribute(hwnd, DWMWA_FULLSCREEN, out bool isFullscreen, Marshal.SizeOf<bool>());
        return hr == 0 && isFullscreen;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
