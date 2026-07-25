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

    [Fact]
    public void NumpadDigitsDisplayLikeTopRowOnes()
    {
        // one display name → duplicate detection sees Ctrl+NumPad1 as Ctrl+1
        var g = HotkeyParser.ToGesture("Ctrl+NumPad1")!;
        Assert.Equal("Ctrl+1", HotkeyParser.Display(g));
    }

    [Theory]
    [InlineData(Key.D3, Key.NumPad3)]
    [InlineData(Key.NumPad7, Key.D7)]
    [InlineData(Key.K, Key.None)]
    [InlineData(Key.F2, Key.None)]
    public void DigitTwinPairsTopRowWithNumpad(Key key, Key twin) =>
        Assert.Equal(twin, HotkeyParser.DigitTwin(key));

    [Theory]
    [InlineData(ModifierKeys.Control, Key.K, "Set aside")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.Z, "Undo")]
    [InlineData(ModifierKeys.Control, Key.OemComma, "Settings")]
    public void TheAppsFixedKeysAreReserved(ModifierKeys mods, Key key, string use) =>
        Assert.Equal(use, HotkeyParser.ReservedUse(mods, key));

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.K)]   // near misses stay free
    [InlineData(ModifierKeys.Control, Key.Z)]
    [InlineData(ModifierKeys.None, Key.K)]
    [InlineData(ModifierKeys.Control, Key.D1)]
    public void NearMissesAreNotReserved(ModifierKeys mods, Key key) =>
        Assert.Null(HotkeyParser.ReservedUse(mods, key));
}
