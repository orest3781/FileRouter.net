using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FileRouter.Wpf.Theme;

/// <summary>Applies a <see cref="ThemePalette"/> to the running app as
/// "Theme.*" brush resources (consumed by Styles.xaml via DynamicResource),
/// follows the OS light/dark preference, and re-applies live when the user
/// changes it — matching the Python original's colorSchemeChanged behavior.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static ThemePalette Current => IsDark ? ThemePalette.Dark : ThemePalette.Light;

    public static void Start(Application app)
    {
        Apply(app, ReadOsPrefersDark());
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                app.Dispatcher.BeginInvoke(() => Apply(app, ReadOsPrefersDark()));
        };
    }

    public static void Apply(Application app, bool dark)
    {
        IsDark = dark;
        var p = Current;
        var r = app.Resources;
        r["Theme.WindowBg"] = Brush(p.WindowBg);
        r["Theme.Surface"] = Brush(p.Surface);
        r["Theme.Text"] = Brush(p.Text);
        r["Theme.SubtleText"] = Brush(p.SubtleText);
        r["Theme.Border"] = Brush(p.Border);
        r["Theme.Accent"] = Brush(p.Accent);
        r["Theme.AccentText"] = Brush(p.AccentText);
        r["Theme.Warning"] = Brush(p.Warning);
        r["Theme.WarningText"] = Brush(p.WarningText);
        r["Theme.Danger"] = Brush(p.Danger);
        r["Theme.DangerText"] = Brush(p.DangerText);
        r["Theme.Success"] = Brush(p.Success);
        r["Theme.StatusAmber"] = Brush(p.StatusAmber);
        r["Theme.TileDefaultBg"] = Brush(p.TileDefaultBg);
        // hover/pressed shades derived once so Styles.xaml stays declarative
        r["Theme.SurfaceHover"] = Brush(Mix(p.Surface, p.Text, 0.08));
        r["Theme.SurfacePressed"] = Brush(Mix(p.Surface, p.Text, 0.16));
        r["Theme.AccentHover"] = Brush(Mix(p.Accent, new Rgb(255, 255, 255), 0.12));

        // Native-templated controls (menus, scrollbars, dialogs) read the
        // SystemColors brush keys — override them so dark mode reaches the
        // parts we don't retemplate.
        r[SystemColors.WindowBrushKey] = Brush(p.Surface);
        r[SystemColors.WindowTextBrushKey] = Brush(p.Text);
        r[SystemColors.ControlBrushKey] = Brush(p.WindowBg);
        r[SystemColors.ControlTextBrushKey] = Brush(p.Text);
        r[SystemColors.MenuBrushKey] = Brush(p.Surface);
        r[SystemColors.MenuTextBrushKey] = Brush(p.Text);
        r[SystemColors.MenuBarBrushKey] = Brush(p.WindowBg);
        r[SystemColors.HighlightBrushKey] = Brush(p.Accent);
        r[SystemColors.HighlightTextBrushKey] = Brush(p.AccentText);
        r[SystemColors.GrayTextBrushKey] = Brush(p.SubtleText);
    }

    public static SolidColorBrush Brush(Rgb c)
    {
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>Blend <paramref name="amount"/> of <paramref name="into"/>
    /// into <paramref name="baseColor"/> — cheap hover/pressed derivation.</summary>
    private static Rgb Mix(Rgb baseColor, Rgb into, double amount) => new(
        (byte)(baseColor.R + (into.R - baseColor.R) * amount),
        (byte)(baseColor.G + (into.G - baseColor.G) * amount),
        (byte)(baseColor.B + (into.B - baseColor.B) * amount));

    private static bool ReadOsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;   // no signal -> light
        }
    }
}
