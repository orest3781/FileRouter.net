using System.Windows.Input;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+1", ModifierKeys.Control, Key.D1)]
    [InlineData("ctrl+shift+f", ModifierKeys.Control | ModifierKeys.Shift, Key.F)]
    [InlineData("Control+9", ModifierKeys.Control, Key.D9)]
    [InlineData("F2", ModifierKeys.None, Key.F2)]
    [InlineData("Alt+Enter", ModifierKeys.Alt, Key.Enter)]
    public void ParsesCommonForms(string text, ModifierKeys mods, Key key)
    {
        Assert.True(HotkeyParser.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Bogus+1")]
    [InlineData("Ctrl+NotAKey")]
    public void RejectsGarbage(string? text) =>
        Assert.False(HotkeyParser.TryParse(text, out _, out _));

    [Fact]
    public void BareLettersParseButCannotGesture()
    {
        Assert.True(HotkeyParser.TryParse("Q", out _, out var k));
        Assert.Equal(Key.Q, k);
        Assert.Null(HotkeyParser.ToGesture("Q"));   // WPF needs a modifier here
    }

    [Fact]
    public void GestureRoundTripsThroughDisplay()
    {
        var g = HotkeyParser.ToGesture("ctrl+shift+2")!;
        Assert.Equal("Ctrl+Shift+2", HotkeyParser.Display(g));
        Assert.Equal("F2", HotkeyParser.Display(HotkeyParser.ToGesture("F2")!));
    }
}
