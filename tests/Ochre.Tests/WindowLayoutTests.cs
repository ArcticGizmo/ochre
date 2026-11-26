using Ochre.Core.Layout;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// windows.json never stops a start: a missing, partial or damaged file reads as no windows, and a bad entry is
/// skipped. And a window comes back on its monitor, or on the primary when that monitor is gone.
/// </summary>
public sealed class WindowLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly string _file;

    private static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    private static readonly PixelRect PrimaryWork = new(0, 0, 1920, 1040);
    private static readonly PixelRect Right = new(1920, 0, 2560, 1440);
    private static readonly PixelRect RightWork = new(1920, 0, 2560, 1400);

    public WindowLayoutTests()
    {
        Directory.CreateDirectory(_root);
        _file = Path.Combine(_root, "windows.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Missing_ReadsAsNoWindows() => Assert.Empty(new WindowLayoutStore(_file).Load());

    [Theory]
    [InlineData("")]
    [InlineData("{\"windows\":[{\"task\":\"T-0001\"")]
    [InlineData("not json")]
    [InlineData("{\"windows\":{}}")]
    [InlineData("[1,2,3]")]
    public void Damaged_ReadsAsNoWindows(string text)
    {
        File.WriteAllText(_file, text);
        Assert.Empty(new WindowLayoutStore(_file).Load());
    }

    [Fact]
    public void BadEntriesAreSkippedOrTrimmed()
    {
        File.WriteAllText(_file, """
            {"windows":[
              42,
              {"task":"T-0002","desktop":"not-a-guid","bounds":{"x":1,"y":2},"scale":"big"},
              {"task":7,"maximized":"yes"}
            ]}
            """);

        var windows = new WindowLayoutStore(_file).Load();

        Assert.Equal(2, windows.Count);
        Assert.Equal(new SavedWindow("T-0002", null, null, false), windows[0]);
        Assert.Equal(new SavedWindow(null, null, null, false), windows[1]);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var desktop = Guid.NewGuid();
        var saved = new[]
        {
            new SavedWindow("T-0042", desktop, new SavedPlacement(new PixelRect(100, 80, 1400, 900), Primary, PrimaryWork, 1.5), false),
            new SavedWindow(null, null, null, true),
        };
        var store = new WindowLayoutStore(_file);

        store.Save(saved);

        Assert.Equal(saved, new WindowLayoutStore(_file).Load());
    }

    [Fact]
    public void SameMonitor_ComesBackExactly()
    {
        var saved = new SavedPlacement(new PixelRect(2100, 50, 1400, 900), Right, RightWork, 1.0);

        var placed = WindowRestore.Place(saved, [new ScreenArea(Primary, PrimaryWork, 1.0, true), new ScreenArea(Right, RightWork, 1.0, false)]);

        Assert.Equal(new PixelRect(2100, 50, 1400, 900), placed);
    }

    [Fact]
    public void MonitorGone_ComesBackOnThePrimary()
    {
        var saved = new SavedPlacement(new PixelRect(2100, 50, 1400, 900), Right, RightWork, 1.0);

        var placed = WindowRestore.Place(saved, [new ScreenArea(Primary, PrimaryWork, 1.0, true)]);

        // Kept its distance from the work area's top-left corner (180 in, 50 down) and its size.
        Assert.Equal(new PixelRect(180, 50, 1400, 900), placed);
    }

    [Fact]
    public void MonitorGone_WindowTooBigForThePrimary_ShrinksToFit()
    {
        var saved = new SavedPlacement(new PixelRect(1920, 0, 2560, 1400), Right, RightWork, 1.0);

        var placed = WindowRestore.Place(saved, [new ScreenArea(Primary, PrimaryWork, 1.0, true)]);

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), placed);
    }

    [Fact]
    public void ScaleChanged_KeepsTheSizeInDips()
    {
        var saved = new SavedPlacement(new PixelRect(100, 100, 1000, 500), Primary, PrimaryWork, 1.0);

        var placed = WindowRestore.Place(saved, [new ScreenArea(Primary, PrimaryWork, 1.5, true)]);

        Assert.Equal(new PixelRect(150, 150, 1500, 750), placed);
    }

    [Fact]
    public void NoScreens_LeavesItToWindows() =>
        Assert.Null(WindowRestore.Place(new SavedPlacement(new PixelRect(0, 0, 10, 10), Primary, PrimaryWork, 1.0), []));
}
