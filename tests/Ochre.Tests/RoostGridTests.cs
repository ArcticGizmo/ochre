using Ochre.Modules.Roost;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Guards <see cref="RoostGrid"/>, one task's grid: placement is the user's (nothing placed moves by itself, a session
/// taken out stays out), new sessions fill empty regions, and a saved grid that doesn't add up reads back safely. The
/// placement rules follow Perch's <c>RoostTabSetTests</c> (9275e0b) for a single tab.
/// </summary>
public class RoostGridTests
{
    private static RoostGrid Grid(params string[] sessions) => RoostGrid.For(sessions, 1.6);

    // An empty region reads as "-".
    private static string[] InReadingOrder(RoostGrid g) => g.Layout.ReadingOrder.Select(r => g.At(r.Id) ?? "-").ToArray();

    [Fact]
    public void ANewGridIsShapedForItsSessionsAndFilledInOrder()
    {
        var g = Grid("a", "b", "c");
        Assert.Equal(RoostGridLayout.FromTemplate(RoostSnapTemplate.MainPlusTwo).GeometryKey, g.Layout.GeometryKey);
        Assert.Equal(["a", "b", "c"], InReadingOrder(g));

        var none = Grid();
        Assert.Equal(RoostGridLayout.Full.Signature, none.Layout.Signature);
        Assert.Empty(none.Cells);

        var seven = Grid("1", "2", "3", "4", "5", "6", "7");
        Assert.Equal(6, seven.Cells.Count);   // the seventh waits outside the grid
    }

    [Fact]
    public void AssigningASessionFromAnotherRegionSwapsTheTwo()
    {
        var g = Grid("a", "b");
        var (left, right) = (g.Layout.ReadingOrder[0].Id, g.Layout.ReadingOrder[1].Id);
        Assert.Null(g.Assign(left, "b"));
        Assert.Equal(["b", "a"], InReadingOrder(g));
        Assert.Equal(right, g.RegionOf("a"));
    }

    [Fact]
    public void AssigningASessionFromOutsideSendsTheOccupantOut()
    {
        var g = Grid("a", "b");
        Assert.Equal("a", g.Assign(g.Layout.ReadingOrder[0].Id, "c"));
        Assert.Equal(["c", "b"], InReadingOrder(g));
        Assert.Null(g.RegionOf("a"));
        Assert.Null(g.Assign(99, "a"));   // no such region: nothing changes
        Assert.Equal(["c", "b"], InReadingOrder(g));
    }

    [Fact]
    public void A_session_taken_out_stays_out_and_only_unseen_sessions_fill_empty_regions()
    {
        var g = Grid("a", "b");
        Assert.True(g.Unassign("a"));
        Assert.False(g.Unassign("a"));

        Assert.False(g.Sync(["a", "b"]));   // a is known: it isn't put back
        Assert.Null(g.RegionOf("a"));

        Assert.True(g.Sync(["a", "b", "c"]));   // c is new and there's an empty region
        Assert.Equal(["c", "b"], InReadingOrder(g));

        Assert.True(g.Sync(["a", "b", "c", "d"]));   // no empty region: d waits outside
        Assert.Null(g.RegionOf("d"));
        Assert.False(g.Sync(["a", "b", "c", "d"]));
    }

    [Fact]
    public void A_session_that_has_gone_leaves_its_region_empty_and_unzoomed()
    {
        var g = Grid("a", "b");
        var region = g.RegionOf("b")!.Value;
        Assert.Equal(region, g.ToggleZoom(region));

        Assert.True(g.Sync(["a"]));
        Assert.Null(g.At(region));
        Assert.Null(g.Zoomed);
        Assert.Equal(2, g.Layout.Regions.Count);   // the layout is the user's; only the session went
    }

    [Fact]
    public void Zoom_toggles_and_ignores_regions_the_grid_does_not_have()
    {
        var g = Grid("a", "b");
        var first = g.Layout.ReadingOrder[0].Id;
        Assert.Equal(first, g.ToggleZoom(first));
        Assert.Null(g.ToggleZoom(first));
        Assert.Null(g.ToggleZoom(99));
        Assert.Equal(first, g.ToggleZoom(first));
        Assert.Null(g.ToggleZoom(null));
    }

    [Fact]
    public void A_preset_keeps_sessions_in_reading_order_and_drops_the_extras()
    {
        var g = Grid("a", "b", "c", "d");   // 2 × 2
        g.ToggleZoom(g.RegionOf("d"));
        var dropped = g.ApplyLayout(RoostGridLayout.FromTemplate(RoostSnapTemplate.Columns2));
        Assert.Equal(["a", "b"], InReadingOrder(g));
        Assert.Equal(["c", "d"], dropped.Order());
        Assert.Null(g.Zoomed);

        var back = g.ApplyLayout(RoostGridLayout.FromTemplate(RoostSnapTemplate.Grid3x2));
        Assert.Empty(back);
        Assert.Equal(["a", "b", "-", "-", "-", "-"], InReadingOrder(g));
    }

    [Fact]
    public void A_saved_grid_round_trips_and_one_that_does_not_add_up_reads_back_safely()
    {
        var g = Grid("a", "b", "c");
        g.Unassign("c");
        g.ToggleZoom(g.RegionOf("a"));
        var copy = RoostGrid.FromState(g.ToState());
        Assert.Equal(InReadingOrder(g), InReadingOrder(copy));
        Assert.Equal(g.Zoomed, copy.Zoomed);
        Assert.False(copy.Sync(["a", "b", "c"]));   // c was taken out before the save; it stays out

        var bad = RoostGrid.FromState(new RoostGridState(
            [new RoostRegion(0, 0, 0, 12, 6)],                                 // doesn't tile: reads as one region
            new Dictionary<int, string> { [0] = "a", [5] = "b" },              // region 5 doesn't exist
            Zoomed: 5,
            Known: []));
        Assert.Equal(RoostGridLayout.Full.Signature, bad.Layout.Signature);
        Assert.Equal(["a"], InReadingOrder(bad));
        Assert.Null(bad.Zoomed);
    }
}
