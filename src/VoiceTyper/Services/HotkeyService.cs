using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoiceTyper.Models;
using VoiceTyper.Native;

namespace VoiceTyper.Services;

public sealed class HotkeyService : IDisposable
{
    private const int DWMWA_FULLSCREEN = 9;
    private const int ConsumePollMs = 20;
    private const int FullscreenPollMs = 500;

    private readonly LowLevelKeyboardHook _hook;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _loopsCts;
    private TaskCompletionSource<bool>? _currentTcs;
    private bool _disposed;

    public VirtualKey Modifier { get; private set; } = VirtualKey.RMenu;
    public VirtualKey Trigger { get; private set; } = VirtualKey.Space;
    public bool IsRecording { get; private set; }
    public Task RecordingTask => _currentTcs?.Task ?? Task.CompletedTask;
    public bool IsReady { get; set; } = false;
    public bool IsTestActive { get; private set; }

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public HotkeyService(LowLevelKeyboardHook hook, SettingsService settings)
    {
        _hook = hook;
        _settings = settings;
        ApplySettings(_settings.Current);
    }

    public void ApplySettings(AppSettings s)
    {
        var newMod = ParseKey(s.HotkeyModifier, VirtualKey.RMenu);
        var newTrig = ParseKey(s.HotkeyTrigger, VirtualKey.Space);

        var modChanged = newMod != Modifier;
        var trigChanged = newTrig != Trigger;

        Modifier = newMod;
        Trigger = newTrig;
        Log.Info($"[Hotkey] applied: modifier={Modifier}, trigger={Trigger} (changed: mod={modChanged}, trig={trigChanged})");
    }

    public void Reconfigure()
    {
        if (IsRecording)
        {
            Log.Warn("[Hotkey] Reconfigure requested during recording, deferred (will apply on next cycle)");
            return;
        }
        ApplySettings(_settings.Current);
    }

    private static VirtualKey ParseKey(string name, VirtualKey fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        if (Enum.TryParse<VirtualKey>(name, ignoreCase: true, out var vk)) return vk;
        Log.Warn($"[Hotkey] unknown virtual key name '{name}', using fallback {fallback}");
        return fallback;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
        if (_loopsCts is not null) return;

        Log.Info("[Hotkey] starting");
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

        Log.Info("[Hotkey] stopping");
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

    public async Task<bool> TestAsync(int timeoutMs, CancellationToken ct = default)
    {
        if (IsTestActive)
        {
            Log.Warn("[Hotkey] TestAsync requested while another test is in progress, ignoring");
            return false;
        }

        var modAtStart = Modifier;
        var trigAtStart = Trigger;

        IsTestActive = true;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnKeyDown(VirtualKey vk)
        {
            if (vk != trigAtStart) return;
            if (!LowLevelKeyboardHook.IsKeyPressed((ushort)modAtStart)) return;
            Log.Info($"[Hotkey] test detected (mod={modAtStart}, trig={trigAtStart})");
            tcs.TrySetResult(true);
        }

        _hook.KeyDown += OnKeyDown;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using (cts.Token.Register(() => tcs.TrySetResult(false)))
            {
                await tcs.Task.ConfigureAwait(false);
            }
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _hook.KeyDown -= OnKeyDown;
            IsTestActive = false;
        }
    }

    private void OnHookKeyDown(VirtualKey vk)
    {
        if (!IsReady) return;
        if (vk != Trigger) return;
        if (IsTestActive) return;
        if (IsRecording) return;
        if (!LowLevelKeyboardHook.IsKeyPressed((ushort)Modifier)) return;

        IsRecording = true;
        _currentTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hook.ConsumeNextKeyDown = true;
        Log.Info($"[Hotkey] recording started (mod={Modifier}, trig={Trigger})");
        RecordingStarted?.Invoke();
    }

    private void OnHookKeyUp(VirtualKey vk)
    {
        if (!IsReady) return;
        if (vk != Trigger) return;
        if (!IsRecording) return;

        IsRecording = false;
        _hook.ConsumeNextKeyDown = false;
        Log.Info("[Hotkey] ConsumeNextKeyDown reset (recording stopped)");
        RecordingStopped?.Invoke();
        _currentTcs?.TrySetResult(true);
        _currentTcs = null;
        Log.Info("[Hotkey] recording stopped");
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
                var isFullscreen = IsForegroundFullscreen() && _settings.Current.PauseOnFullscreen;
                if (isFullscreen != wasFullscreen)
                {
                    _hook.IsPaused = isFullscreen;
                    if (isFullscreen)
                    {
                        Log.Info("[Hotkey] paused (fullscreen detected)");
                    }
                    else
                    {
                        Log.Info("[Hotkey] resumed (no longer fullscreen)");
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
