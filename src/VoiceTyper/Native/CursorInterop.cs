using System.Runtime.InteropServices;

namespace VoiceTyper.Native;

public static class CursorInterop
{
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public static bool TryGetCursorPos(out int x, out int y)
    {
        if (GetCursorPos(out var pt))
        {
            x = pt.x;
            y = pt.y;
            return true;
        }
        x = 0;
        y = 0;
        return false;
    }

    public static RECT GetWorkArea(int x, int y)
    {
        var pt = new POINT { x = x, y = y };
        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }

        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }
        return info.rcWork;
    }
}
