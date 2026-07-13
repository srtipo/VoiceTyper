using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using VoiceTyper.Services;

namespace VoiceTyper.Native;

public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const int HC_ACTION = 0;
    private const uint PM_REMOVE = 0x0001;

    private IntPtr _hookId = IntPtr.Zero;
    private uint _hookThreadId;
    private LowLevelKeyboardProc? _proc;
    private readonly HashSet<VirtualKey> _downKeys = new();
    private Thread? _hookThread;
    private readonly ManualResetEventSlim _installedSignal = new();
    private Exception? _installError;
    private bool _disposed;

    public event Action<VirtualKey>? KeyDown;
    public event Action<VirtualKey>? KeyUp;

    public bool IsPaused { get; set; }
    public bool IsInstalled => _hookId != IntPtr.Zero;
    public bool ConsumeNextKeyDown { get; set; }

    public void Install()
    {
        if (IsInstalled) return;
        if (_disposed) throw new ObjectDisposedException(nameof(LowLevelKeyboardHook));

        Log.Info("[Hook] installing");
        _installError = null;
        _installedSignal.Reset();

        _hookThread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "VoiceTyper Keyboard Hook"
        };
        _hookThread.Start();

        if (!_installedSignal.Wait(TimeSpan.FromSeconds(5)))
        {
            Log.Error("[Hook] install timed out after 5s");
            throw new TimeoutException("Keyboard hook installation timed out");
        }

        if (_installError != null)
        {
            Log.Error($"[Hook] install failed: {_installError.Message}");
            throw _installError;
        }

        Log.Info("[Hook] installed");
    }

    private void HookThreadProc()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            var hMod = GetModuleHandle(null);
            _proc = HookProc;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
            if (_hookId == IntPtr.Zero)
            {
                _proc = null;
                _installError = new Win32Exception(Marshal.GetLastWin32Error());
                _installedSignal.Set();
                return;
            }
            _installedSignal.Set();

            while (_hookId != IntPtr.Zero)
            {
                if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_QUIT) break;
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        catch (Exception ex)
        {
            _installError = ex;
            _installedSignal.Set();
        }
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;

        Log.Info("[Hook] uninstalling");
        var hookId = _hookId;
        _hookId = IntPtr.Zero;
        PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        UnhookWindowsHookEx(hookId);
        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _hookThread = null;
        _proc = null;
        _downKeys.Clear();
        ConsumeNextKeyDown = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
        _installedSignal.Dispose();
        GC.SuppressFinalize(this);
    }

    public static bool IsKeyPressed(ushort vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (_hookId == IntPtr.Zero) return (IntPtr)0;
        if (nCode != HC_ACTION) return CallNextHookEx(_hookId, nCode, wParam, lParam);
        if (IsPaused) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var vk = (VirtualKey)kb.vkCode;
        var wm = (int)wParam;
        var isDown = wm == WM_KEYDOWN || wm == WM_SYSKEYDOWN;
        var isUp = wm == WM_KEYUP || wm == WM_SYSKEYUP;

        if (isDown)
        {
            if (_downKeys.Add(vk))
            {
                KeyDown?.Invoke(vk);
            }

            if (ConsumeNextKeyDown)
            {
                ConsumeNextKeyDown = false;
                return (IntPtr)1;
            }
        }
        else if (isUp)
        {
            _downKeys.Remove(vk);
            KeyUp?.Invoke(vk);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint removeMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
}
