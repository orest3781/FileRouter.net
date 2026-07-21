namespace FileRouter.Wpf.Theme;

/// <summary>A color as plain bytes — no WPF types, so palette logic and the
/// WCAG contrast contract stay unit-testable without a dispatcher.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>The theme token tables (light + dark) and the WCAG 2.1 contrast
/// math. Every text/background pairing shipped here is enforced to >= 4.5:1
/// by ThemeTests — the same contract the Python original kept.</summary>
public sealed record ThemePalette(
    Rgb WindowBg,      // window background
    Rgb Surface,       // cards, inputs, grids
    Rgb Text,          // primary text on WindowBg/Surface
    Rgb SubtleText,    // secondary text (filenames, hints)
    Rgb Border,        // control borders, splitters
    Rgb Accent,        // primary action (Start, OK)
    Rgb AccentText,    // text on Accent
    Rgb Warning,       // warning banner background
    Rgb WarningText,   // text on Warning
    Rgb Danger,        // alert red (flashing tiles, illegal-name preview)
    Rgb DangerText,    // text on Danger
    Rgb Success,       // positive accents (Done summary)
    Rgb StatusAmber,   // the amber status line, readable on WindowBg
    Rgb TileDefaultBg) // dashboard tile with no configured color
{
    public static ThemePalette Light { get; } = new(
        WindowBg: new(250, 250, 250),
        Surface: new(255, 255, 255),
        Text: new(26, 26, 26),
        SubtleText: new(95, 95, 95),
        Border: new(208, 208, 208),
        Accent: new(21, 101, 192),
        AccentText: new(255, 255, 255),
        Warning: new(255, 236, 179),
        WarningText: new(102, 60, 0),
        Danger: new(192, 57, 43),
        DangerText: new(255, 255, 255),
        Success: new(46, 125, 50),
        StatusAmber: new(146, 90, 4),
        TileDefaultBg: new(225, 225, 225));

    public static ThemePalette Dark { get; } = new(
        WindowBg: new(30, 30, 30),
        Surface: new(45, 45, 45),
        Text: new(235, 235, 235),
        SubtleText: new(170, 170, 170),
        Border: new(85, 85, 85),
        Accent: new(28, 116, 210),
        AccentText: new(255, 255, 255),
        Warning: new(84, 62, 8),
        WarningText: new(255, 224, 130),
        Danger: new(192, 57, 43),
        DangerText: new(255, 255, 255),
        Success: new(46, 125, 50),
        StatusAmber: new(240, 173, 78),
        TileDefaultBg: new(60, 60, 60));

    // ---------------------------------------------------------- WCAG 2.1 math

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    public static double Luminance(Rgb c) =>
        0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>WCAG 2.1 contrast ratio, 1..21. AA for normal text is 4.5.</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    /// <summary>Black or white — whichever actually contrasts more against the
    /// background. The single source of truth for text on route buttons and
    /// dashboard tiles (the WinForms app duplicated a cruder luminance shortcut
    /// in three places).</summary>
    public static Rgb IdealForeground(Rgb bg)
    {
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);
        return ContrastRatio(black, bg) >= ContrastRatio(white, bg) ? black : white;
    }

    /// <summary>Parse a config color string ("#2e7d32" or a CSS name) without
    /// WPF types. Null for blank/invalid — callers fall back to the theme.</summary>
    public static Rgb? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(text.Trim());
            return new Rgb(c.R, c.G, c.B);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
