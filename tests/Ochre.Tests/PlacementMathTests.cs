using Ochre.Core.Layout;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Covers <see cref="PlacementMath"/>, the corner-anchored geometry behind reopening a window where it was. All
/// coordinates are physical pixels; offsets are DIP.
/// </summary>
public class PlacementMathTests
{
    // A primary-ish work area (origin 0,0) and a left-of-primary secondary monitor with negative X, to catch any code
    // that confuses an offset for an absolute coordinate.
    private const int PrimW = 1920, PrimH = 1040; // 1080 screen minus a taskbar
    private const int PhysW = 280, PhysH = 400;

    [Theory]
    [InlineData(200, 150, 1.0)]
    [InlineData(200, 150, 1.5)]
    [InlineData(200, 150, 2.0)]
    [InlineData(1920 - 280 - 200, 150, 1.5)] // near right edge
    [InlineData(200, 600, 1.5)] // lower half -> bottom (title bar) anchored
    [InlineData(1920 - 280 - 200, 600, 1.25)] // bottom-right
    public void FromPosition_ThenToPosition_RoundTrips(int x, int y, double scale)
    {
        var placement = PlacementMath.FromPosition(x, y, 0, 0, PrimW, PrimH, scale, PhysW, PhysH);
        var (rx, ry) = PlacementMath.ToPosition(placement, 0, 0, PrimW, PrimH, scale, PhysW, PhysH);

        Assert.Equal(x, rx);
        Assert.Equal(y, ry);
    }

    [Fact]
    public void FromPosition_PicksNearestCorner()
    {
        var tl = PlacementMath.FromPosition(40, 30, 0, 0, PrimW, PrimH, 1.0, PhysW, PhysH);
        Assert.Equal(HAnchor.Left, tl.HAnchor);
        Assert.Equal(VAnchor.Top, tl.VAnchor);
        Assert.Equal(40, tl.OffsetX);
        Assert.Equal(30, tl.OffsetY);

        // Bottom-right: X measures the window's right edge from the right; Y measures the title bar (top edge) from
        // the bottom, independent of the window's height.
        var brX = PrimW - PhysW - 60;
        var brY = PrimH - 425;
        var br = PlacementMath.FromPosition(brX, brY, 0, 0, PrimW, PrimH, 1.0, PhysW, PhysH);
        Assert.Equal(HAnchor.Right, br.HAnchor);
        Assert.Equal(VAnchor.Bottom, br.VAnchor);
        Assert.Equal(60, br.OffsetX);
        Assert.Equal(425, br.OffsetY);
    }

    [Fact]
    public void ToPosition_ScalesOffsetByDpi()
    {
        var p = new Placement(HAnchor.Left, VAnchor.Top, 16, 16);
        var (x, y) = PlacementMath.ToPosition(p, 0, 0, PrimW, PrimH, 1.5, PhysW, PhysH);
        Assert.Equal(24, x);
        Assert.Equal(24, y);
    }

    [Fact]
    public void ToPosition_RightBottomAnchorsMeasureFromFarEdges()
    {
        var p = new Placement(HAnchor.Right, VAnchor.Bottom, 16, 432);
        var (x, y) = PlacementMath.ToPosition(p, 0, 0, PrimW, PrimH, 1.0, PhysW, PhysH);
        Assert.Equal(PrimW - PhysW - 16, x);
        Assert.Equal(PrimH - PhysH - 32, y);
    }

    [Fact]
    public void ToPosition_BottomAnchor_TitleBarYIndependentOfHeight()
    {
        var p = new Placement(HAnchor.Right, VAnchor.Bottom, 0, 300);
        var (_, yShort) = PlacementMath.ToPosition(p, 0, 0, PrimW, PrimH, 1.0, PhysW, 120);
        var (_, yTall) = PlacementMath.ToPosition(p, 0, 0, PrimW, PrimH, 1.0, PhysW, 260);
        Assert.Equal(PrimH - 300, yShort);
        Assert.Equal(yShort, yTall);
    }

    [Fact]
    public void ToPosition_BottomAnchor_TooTallClampsBottomFlush()
    {
        var p = new Placement(HAnchor.Right, VAnchor.Bottom, 0, 100);
        var (_, y) = PlacementMath.ToPosition(p, 0, 0, PrimW, PrimH, 1.0, PhysW, PhysH);
        Assert.Equal(PrimH - PhysH, y);
    }

    [Fact]
    public void RoundTrip_OnSecondaryMonitorWithNegativeOrigin()
    {
        const int waX = -1920, waY = 0, waW = 1920, waH = 1080;
        const int x = -1920 + 300, y = 500;

        var p = PlacementMath.FromPosition(x, y, waX, waY, waW, waH, 1.0, PhysW, PhysH);
        var (rx, ry) = PlacementMath.ToPosition(p, waX, waY, waW, waH, 1.0, PhysW, PhysH);
        Assert.Equal(x, rx);
        Assert.Equal(y, ry);
    }

    [Fact]
    public void Clamp_KeepsWindowInsideWorkArea()
    {
        var (x, y) = PlacementMath.Clamp(9999, 9999, 0, 0, PrimW, PrimH, PhysW, PhysH);
        Assert.Equal(PrimW - PhysW, x);
        Assert.Equal(PrimH - PhysH, y);

        (x, y) = PlacementMath.Clamp(-500, -500, 0, 0, PrimW, PrimH, PhysW, PhysH);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Clamp_WindowLargerThanWorkArea_PinsToOrigin()
    {
        var (x, y) = PlacementMath.Clamp(50, 50, 100, 100, 200, 200, 400, 400);
        Assert.Equal(100, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void ToPosition_ClampsAnOverlargeOffsetBackOnScreen()
    {
        var p = new Placement(HAnchor.Left, VAnchor.Top, 0, 100000);
        var (_, y) = PlacementMath.ToPosition(p, 0, 0, PrimW, PrimH, 1.0, PhysW, PhysH);
        Assert.Equal(PrimH - PhysH, y);
    }

    [Fact]
    public void PickMostOverlapping_ChoosesTheScreenTheWindowSitsOnMost()
    {
        var screens = new (int, int, int, int)[] { (0, 0, 1920, 1080), (1920, 0, 1920, 1080) };
        Assert.Equal(1, PlacementMath.PickMostOverlapping(screens, 1900, 100, 280, 400));
        Assert.Equal(0, PlacementMath.PickMostOverlapping(screens, 1700, 100, 280, 400));
    }

    [Fact]
    public void PickMostOverlapping_ResolutionChangeStillResolvesSameMonitor()
    {
        var shrunk = new (int, int, int, int)[] { (0, 0, 1280, 720) };
        Assert.Equal(0, PlacementMath.PickMostOverlapping(shrunk, 1000, 40, 280, 400));
    }

    [Fact]
    public void PickMostOverlapping_StrandedOffEveryScreen_FallsBackToNearest()
    {
        var screens = new (int, int, int, int)[] { (0, 0, 1920, 1080), (-1920, 0, 1920, 1080) };
        Assert.Equal(0, PlacementMath.PickMostOverlapping(screens, 5000, 100, 280, 400));
        Assert.Equal(-1, PlacementMath.PickMostOverlapping(Array.Empty<(int, int, int, int)>(), 0, 0, 1, 1));
    }

    [Fact]
    public void CornerRelative_SurvivesLargeToSmallToLarge()
    {
        const int bigW = 3840, bigH = 2120, smallW = 1280, smallH = 680;
        var placement = PlacementMath.FromPosition(bigW - PhysW - 16, 24, 0, 0, bigW, bigH, 1.0, PhysW, PhysH);

        var (sx, _) = PlacementMath.ToPosition(placement, 0, 0, smallW, smallH, 1.0, PhysW, PhysH);
        Assert.Equal(smallW - PhysW - 16, sx);

        var (bx, by) = PlacementMath.ToPosition(placement, 0, 0, bigW, bigH, 1.0, PhysW, PhysH);
        Assert.Equal(bigW - PhysW - 16, bx);
        Assert.Equal(24, by);
    }
}
