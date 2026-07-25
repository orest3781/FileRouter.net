using System.Windows.Input;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.ViewModels;

/// <summary>One destination button on the Processing screen: label with
/// suffix + hotkey, config color with a WCAG-picked foreground, disabled with
/// a readable reason when the destination is unusable.</summary>
public sealed class RouteButtonViewModel : ObservableObject
{
    /// <summary>True on the one button Enter would press right now (the
    /// last-used route, when enter_commits is on) — shown as a ⏎ badge.</summary>
    private bool _isEnterTarget;
    public bool IsEnterTarget { get => _isEnterTarget; internal set => Set(ref _isEnterTarget, value); }

    public int Index { get; }
    public string Label { get; }
    public bool Enabled { get; }
    public string? DisabledReason { get; }
    public Rgb Back { get; }
    public Rgb Fore { get; }
    public KeyGesture? Gesture { get; }

    /// <param name="problem">Result of <see cref="Config.ValidateRoute"/> for
    /// this route, gathered off the UI thread — the probe touches the
    /// destination folder, a network round trip on SMB shares.</param>
    public RouteButtonViewModel(int index, Route route, ThemePalette palette, string problem)
    {
        Index = index;

        // configured hotkey binds when parseable; else the classic Ctrl+1-9
        Gesture = HotkeyParser.ToGesture(route.Hotkey)
            ?? (index < 9 ? new KeyGesture(Key.D1 + index, ModifierKeys.Control) : null);
        var gestureText = Gesture is null ? "" : HotkeyParser.Display(Gesture);

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
