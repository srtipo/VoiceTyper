using System.Runtime.InteropServices;
using VoiceTyper.Services;

namespace VoiceTyper.Native;

public static class SendInputInterop
{
    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public HARDWAREINPUT hi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static int SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            inputs[i * 2].type = INPUT_KEYBOARD;
            inputs[i * 2].ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = KEYEVENTF_UNICODE
            };
            inputs[i * 2 + 1].type = INPUT_KEYBOARD;
            inputs[i * 2 + 1].ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
            };
        }

        Log.Info($"[Inject] SendInput Unicode ({text.Length} chars, {inputs.Length} events) from thread {Thread.CurrentThread.ManagedThreadId}");
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            Log.Error($"[Inject] SendInput Unicode failed: {Marshal.GetLastWin32Error()}");
            return 0;
        }
        Log.Info($"[Inject] SendInput Unicode returned {sent}/{inputs.Length}");
        return (int)sent;
    }
}

public static class ClipboardInjector
{
    public const int WM_PASTE = 0x0302;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public static bool SendPaste()
    {
        var hwnd = GetFocus();
        if (hwnd == IntPtr.Zero)
        {
            hwnd = GetForegroundWindow();
        }
        if (hwnd == IntPtr.Zero)
        {
            Log.Error("[Inject] no focused/foreground window for WM_PASTE");
            return false;
        }

        Log.Info($"[Inject] SendMessage WM_PASTE to hwnd=0x{hwnd:X} from thread {Thread.CurrentThread.ManagedThreadId}");
        SendMessage(hwnd, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
        return true;
    }
}
