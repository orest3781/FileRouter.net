using System.Windows;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

public class PanMathTests
{
    [Fact]
    public void DragDownScrollsUpSoContentFollowsTheHand()
    {
        var (v, h) = PanMath.WheelDeltas(dx: 0, dy: 50);
        Assert.True(v > 0);    // positive wheel = scroll up = content moves down
        Assert.Equal(0, h);
    }

    [Fact]
    public void DragRightScrollsLeftSoContentFollowsTheHand()
    {
        var (v, h) = PanMath.WheelDeltas(dx: 50, dy: 0);
        Assert.Equal(0, v);
        Assert.True(h < 0);    // negative h-wheel = scroll left = content moves right
    }

    [Fact]
    public void DeltasScaleRoughlyOneToOne()
    {
        var (v, _) = PanMath.WheelDeltas(0, 100);
        Assert.InRange(v, 100, 140);   // ~1.2x: a notch (120) ≈ 100px in Chromium
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(4, 0, false)]    // wobble, still a click
    [InlineData(5, 0, true)]
    [InlineData(3, 4, true)]     // 5px diagonal
    public void DragThresholdSeparatesClicksFromPans(int dx, int dy, bool drags) =>
        Assert.Equal(drags, PanMath.ExceedsDragThreshold(dx, dy));

    [Fact]
    public void PanZoneExcludesToolbarAndScrollbar()
    {
        var viewer = new Rect(100, 200, 800, 600);
        var zone = PanMath.PanZone(viewer, 1.0, 1.0);

        Assert.Equal(100, zone.X);
        Assert.Equal(200 + PanMath.ToolbarDip, zone.Y);
        Assert.Equal(800 - PanMath.ScrollbarDip, zone.Width);
        Assert.Equal(600 - PanMath.ToolbarDip, zone.Height);

        Assert.False(zone.Contains(500, 210));   // toolbar strip: real clicks
        Assert.False(zone.Contains(890, 400));   // scrollbar edge: real clicks
        Assert.True(zone.Contains(500, 400));    // the document: pannable
    }

    [Fact]
    public void PanZoneScalesWithDpi()
    {
        var viewer = new Rect(0, 0, 800, 600);
        var zone = PanMath.PanZone(viewer, 1.5, 1.5);
        Assert.Equal(PanMath.ToolbarDip * 1.5, zone.Y);
        Assert.Equal(800 - PanMath.ScrollbarDip * 1.5, zone.Width);
    }

    [Fact]
    public void TinyViewerNeverGoesNegative()
    {
        var zone = PanMath.PanZone(new Rect(0, 0, 10, 10), 2.0, 2.0);
        Assert.Equal(0, zone.Width);
        Assert.Equal(0, zone.Height);
    }
}
