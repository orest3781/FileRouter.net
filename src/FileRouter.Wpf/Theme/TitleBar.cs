using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileRouter.Wpf.Theme;

/// <summary>Keeps every window's title bar on the app theme — DWM leaves
/// Win32 title bars light unless the app opts in per window, so dark mode
/// used to ship with a glaring white strip on top.</summary>
internal static class TitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    private const int UseImmersiveDarkMode = 20;      // Windows 10 20H1+
    private const int UseImmersiveDarkModeOld = 19;   // pre-20H1 builds

    private static bool _hooked;

    /// <summary>Class-level hook: every window that loads — including ones
    /// opened later — gets the current theme's title bar.</summary>
    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((Window)sender)));
    }

    /// <summary>Re-theme every open window (live theme switch).</summary>
    public static void ApplyAll(Application app)
    {
        foreach (Window w in app.Windows) Apply(w);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = ThemeManager.IsDark ? 1 : 0;
        // unsupported OS builds just say no — the light bar is the fallback
        if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeOld, ref dark, sizeof(int));
    }
}
