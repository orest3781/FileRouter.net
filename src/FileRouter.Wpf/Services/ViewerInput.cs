using System.Runtime.InteropServices;
using System.Windows;

namespace FileRouter.Wpf.Services;

/// <summary>The gesture arithmetic, kept pure so it's headless-testable:
/// hand-drag panning means the CONTENT follows the cursor, and the pan zone
/// excludes Edge's PDF toolbar (top) and scrollbar (right edge).</summary>
public static class PanMath
{
    /// <summary>Wheel units per dragged pixel. One wheel notch (120) scrolls
    /// Chromium ~100px, so ~1.2 gives close to 1:1 hand-drag feel.</summary>
    public const double Scale = 1.2;

    /// <summary>Device pixels Edge's PDF toolbar occupies (DIPs, pre-scale).</summary>
    public const double ToolbarDip = 56;
    public const double ScrollbarDip = 24;

    /// <summary>Cursor moved (dx, dy) → (vertical, horizontal) wheel deltas.
    /// Drag down = content moves down = scroll up = positive wheel; drag
    /// right = content moves right = scroll left = negative h-wheel.</summary>
    public static (int Vertical, int Horizontal) WheelDeltas(int dx, int dy) =>
        ((int)(dy * Scale), (int)(-dx * Scale));

    /// <summary>A press that moves less than this is a click, not a pan.</summary>
    public static bool ExceedsDragThreshold(int dx, int dy) => dx * dx + dy * dy > 16;

    /// <summary>The draggable document area: the viewer bounds minus the
    /// toolbar strip and the scrollbar edge (those still need real clicks).
    /// All values in device pixels.</summary>
    public static Rect PanZone(Rect viewerDevice, double dpiScaleX, double dpiScaleY)
    {
        var top = ToolbarDip * dpiScaleY;
        var right = ScrollbarDip * dpiScaleX;
        var width = Math.Max(0, viewerDevice.Width - right);
        var height = Math.Max(0, viewerDevice.Height - top);
        return new Rect(viewerDevice.X, viewerDevice.Y + top, width, height);
    }
}

/// <summary>Viewer gestures the Python app's users have in their fingers,
/// grafted onto Edge's PDF viewer: Shift+scroll zooms (anchored at the
/// cursor) and left-drag pans. WebView2 hosts its own HWNDs, so WPF never
/// sees this input — a low-level mouse hook remaps it instead:
///
///   Shift+wheel  -> the same wheel message with MK_CONTROL (Edge zooms)
///   left-drag    -> wheel messages proportional to the drag (Edge scrolls)
///   click        -> replayed to Edge untouched (toolbar clicks still work)
///
/// Only gestures inside a registered pan zone are touched; everything else
/// passes straight through.</summary>
public static class ViewerInputEnhancer
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const int MkLButton = 0x0001;
    private const int MkControl = 0x0008;
    private const int MkShift = 0x0004;
    private const int VkShift = 0x10;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelData
    {
        public NativePoint Point;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    // the delegate must outlive the hook — a local would be collected
    private static readonly HookProc Proc = OnMouse;
    private static IntPtr _hook;
    private static readonly List<Func<Rect?>> Zones = new();

    // drag state (all device pixels)
    private static bool _pending;      // button down in zone, not yet a drag
    private static bool _panning;
    private static NativePoint _down;
    private static NativePoint _last;
    private static IntPtr _target;

    /// <summary>Register a pan-zone provider (device-pixel rect, or null when
    /// the viewer isn't interactive). Installs the hook on first use.</summary>
    public static void Register(Func<Rect?> zone)
    {
        Zones.Add(zone);
        if (_hook == IntPtr.Zero)
            _hook = SetWindowsHookExW(WhMouseLl, Proc, IntPtr.Zero, 0);
    }

    public static void Unregister(Func<Rect?> zone)
    {
        Zones.Remove(zone);
        if (Zones.Count == 0 && _hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static bool InZone(NativePoint p)
    {
        foreach (var zone in Zones)
        {
            Rect? r = null;
            try { r = zone(); } catch (Exception) { /* a dying window's zone */ }
            if (r is { } rect && rect.Contains(p.X, p.Y)) return true;
        }
        return false;
    }

    private static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<MouseLowLevelData>(lParam);
            switch ((int)wParam)
            {
                case WmMouseWheel:
                    if ((GetKeyState(VkShift) & 0x8000) != 0 && InZone(data.Point))
                    {
                        // Shift+scroll -> Ctrl+scroll: Edge zooms at the cursor
                        var delta = (short)(data.MouseData >> 16);
                        var target = WindowFromPoint(data.Point);
                        PostMessageW(target, WmMouseWheel,
                            MakeWheelParam(delta, MkControl),
                            MakePointParam(data.Point.X, data.Point.Y));
                        return (IntPtr)1;
                    }
                    break;

                case WmLButtonDown:
                    if (InZone(data.Point))
                    {
                        _pending = true;
                        _panning = false;
                        _down = data.Point;
                        _last = data.Point;
                        _target = WindowFromPoint(data.Point);
                        return (IntPtr)1;   // held back; replayed if it's a click
                    }
                    break;

                case WmMouseMove:
                    if (_pending || _panning)
                    {
                        if (_pending && PanMath.ExceedsDragThreshold(
                                data.Point.X - _down.X, data.Point.Y - _down.Y))
                        {
                            _pending = false;
                            _panning = true;
                        }
                        if (_panning)
                        {
                            var (v, h) = PanMath.WheelDeltas(
                                data.Point.X - _last.X, data.Point.Y - _last.Y);
                            if (v != 0)
                                PostMessageW(_target, WmMouseWheel,
                                    MakeWheelParam(v, 0),
                                    MakePointParam(data.Point.X, data.Point.Y));
                            if (h != 0)
                                PostMessageW(_target, WmMouseHWheel,
                                    MakeWheelParam(h, 0),
                                    MakePointParam(data.Point.X, data.Point.Y));
                            _last = data.Point;
                        }
                        // never swallow moves — that would freeze the cursor
                    }
                    break;

                case WmLButtonUp:
                    if (_pending)
                    {
                        // no drag happened: it was a click — replay it so the
                        // press-position pixel (a link, a page) still gets it
                        var client = _down;
                        ScreenToClient(_target, ref client);
                        var pos = MakePointParam(client.X, client.Y);
                        PostMessageW(_target, WmLButtonDown, (IntPtr)MkLButton, pos);
                        PostMessageW(_target, WmLButtonUp, IntPtr.Zero, pos);
                        _pending = false;
                        return (IntPtr)1;
                    }
                    if (_panning)
                    {
                        _panning = false;
                        return (IntPtr)1;
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // a hook must never take the mouse down with it
            _pending = _panning = false;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static IntPtr MakeWheelParam(int delta, int keys) =>
        (IntPtr)(((delta & 0xFFFF) << 16) | (keys & 0xFFFF));

    private static IntPtr MakePointParam(int x, int y) =>
        (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
}
