using System.Windows.Input;
using FileRouter.Core;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.ViewModels;

/// <summary>One destination button on the Processing screen: label with
/// suffix + hotkey, config color with a WCAG-picked foreground, disabled with
/// a readable reason when the destination is unusable.</summary>
public sealed class RouteButtonViewModel
{
    public int Index { get; }
    public string Label { get; }
    public bool Enabled { get; }
    public string? DisabledReason { get; }
    public Rgb Back { get; }
    public Rgb Fore { get; }
    public KeyGesture? Gesture { get; }

    public RouteButtonViewModel(int index, Route route, ThemePalette palette)
    {
        Index = index;

        // configured hotkey binds when parseable; else the classic Ctrl+1-9
        Gesture = HotkeyParser.ToGesture(route.Hotkey)
            ?? (index < 9 ? new KeyGesture(Key.D1 + index, ModifierKeys.Control) : null);
        var gestureText = Gesture is null ? "" : HotkeyParser.Display(Gesture);

        var problem = Config.ValidateRoute(route);
        Enabled = problem.Length == 0;
        DisabledReason = Enabled ? null : problem;

        Label = route.Label
            + (route.AppendSuffix && route.Suffix.Length > 0 ? $"   ·   {route.Suffix}" : "")
            + (gestureText.Length > 0 ? $"   ·   {gestureText}" : "")
            + (Enabled ? "" : "   (unavailable)");

        var back = ThemePalette.ParseColor(route.Color);
        Back = back ?? palette.Surface;
        Fore = back is { } b ? ThemePalette.IdealForeground(b) : palette.Text;
    }
}
