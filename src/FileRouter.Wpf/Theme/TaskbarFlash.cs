using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileRouter.Wpf.Theme;

/// <summary>Flashes a window's taskbar button to pull the eye when a new
/// alert lands and the app isn't in focus — the "something needs you" nudge
/// that a dashboard tile alone can't give.</summary>
internal static class TaskbarFlash
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private const uint FLASHW_TRAY = 2;      // flash the taskbar button
    private const uint FLASHW_TIMERNOFG = 12; // until the window comes to the foreground

    public static void Flash(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var fw = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        FlashWindowEx(ref fw);
    }
}
