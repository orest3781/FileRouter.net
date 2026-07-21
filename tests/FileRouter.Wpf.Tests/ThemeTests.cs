using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.Tests;

/// <summary>The visual contract: every text pairing the theme ships meets
/// WCAG AA (4.5:1) in BOTH schemes. Ported from the Python app's theme tests.</summary>
public class ThemeTests
{
    public static IEnumerable<object[]> TextPairs()
    {
        foreach (var p in new[] { ThemePalette.Light, ThemePalette.Dark })
        {
            yield return new object[] { p.Text, p.WindowBg };
            yield return new object[] { p.Text, p.Surface };
            yield return new object[] { p.SubtleText, p.WindowBg };
            yield return new object[] { p.AccentText, p.Accent };
            yield return new object[] { p.WarningText, p.Warning };
            yield return new object[] { p.DangerText, p.Danger };
            yield return new object[] { p.StatusAmber, p.WindowBg };
        }
    }

    [Theory, MemberData(nameof(TextPairs))]
    public void EveryTextPairingMeetsWcagAa(Rgb fg, Rgb bg) =>
        Assert.True(ThemePalette.ContrastRatio(fg, bg) >= 4.5,
            $"{fg} on {bg} = {ThemePalette.ContrastRatio(fg, bg):F2}");

    [Theory]
    [InlineData(46, 125, 50)]    // demo green route
    [InlineData(21, 101, 192)]   // demo blue route
    [InlineData(192, 57, 43)]    // alert red
    [InlineData(255, 255, 255)]
    [InlineData(30, 30, 30)]
    public void IdealForegroundAlwaysMeetsAaOnRealisticBackgrounds(byte r, byte g, byte b)
    {
        var bg = new Rgb(r, g, b);
        var fg = ThemePalette.IdealForeground(bg);
        Assert.True(ThemePalette.ContrastRatio(fg, bg) >= 4.5,
            $"ideal {fg} on {bg} = {ThemePalette.ContrastRatio(fg, bg):F2}");
    }

    [Fact]
    public void ContrastRatioMatchesKnownAnchors()
    {
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);
        Assert.Equal(21.0, ThemePalette.ContrastRatio(black, white), 1);
        Assert.Equal(1.0, ThemePalette.ContrastRatio(white, white), 3);
        // symmetry
        Assert.Equal(
            ThemePalette.ContrastRatio(black, white),
            ThemePalette.ContrastRatio(white, black), 6);
    }

    [Fact]
    public void ParseColorHandlesHexNamesAndGarbage()
    {
        Assert.Equal(new Rgb(46, 125, 50), ThemePalette.ParseColor("#2e7d32"));
        Assert.Equal(new Rgb(255, 0, 0), ThemePalette.ParseColor("red"));
        Assert.Null(ThemePalette.ParseColor(""));
        Assert.Null(ThemePalette.ParseColor(null));
        Assert.Null(ThemePalette.ParseColor("not-a-color"));
    }
}
