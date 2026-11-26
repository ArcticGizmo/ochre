using System.Text.Json;
using System.Text.Json.Nodes;
using Ochre.Core.Claude;

namespace Ochre.Core.Layout;

/// <summary>A rectangle in physical pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public (int X, int Y, int W, int H) Tuple => (X, Y, Width, Height);
}

/// <summary>Where a window was, with the monitor it was on, so it can be put back on that monitor or a stand-in.</summary>
/// <param name="Bounds">The window's normal (not maximised) bounds.</param>
/// <param name="Monitor">The whole monitor it was on.</param>
/// <param name="WorkArea">That monitor's work area (less the taskbar).</param>
/// <param name="Scale">That monitor's scale (DPI / 96).</param>
public sealed record SavedPlacement(PixelRect Bounds, PixelRect Monitor, PixelRect WorkArea, double Scale);

/// <summary>One window as it was when Ochre last saved the layout.</summary>
/// <param name="Task">The task it showed, or null for none.</param>
/// <param name="Desktop">The virtual desktop it was on, when Windows could say.</param>
/// <param name="Placement">Null when its place isn't known (a dev run that kept windows off-screen).</param>
public sealed record SavedWindow(string? Task, Guid? Desktop, SavedPlacement? Placement, bool Maximized);

/// <summary>A monitor as the shell sees it now.</summary>
public sealed record ScreenArea(PixelRect Bounds, PixelRect WorkArea, double Scale, bool Primary);

/// <summary>
/// The open windows, kept in one small JSON file in the local app data folder (<c>windows.json</c>) so they reopen
/// on their desktops after a restart. Replaced atomically. A missing, partial or damaged file reads as no windows,
/// and an entry that doesn't parse is skipped; the window layout is never worth failing a start over.
/// </summary>
public sealed class WindowLayoutStore(string path)
{
    private const int MaxWindows = 64;

    public IReadOnlyList<SavedWindow> Load()
    {
        var windows = new List<SavedWindow>();
        if (AtomicFile.ReadShared(path) is not { Length: > 0 } json) return windows;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["windows"] is not JsonArray list) return windows;
            foreach (var node in list)
            {
                if (windows.Count >= MaxWindows) break;
                if (node is JsonObject o && ReadWindow(o) is { } w) windows.Add(w);
            }
        }
        catch (JsonException)
        {
            // Damaged: start with a fresh layout.
        }
        return windows;
    }

    /// <summary>Best effort; a failed write is retried by the next save.</summary>
    public void Save(IReadOnlyList<SavedWindow> windows)
    {
        var list = new JsonArray();
        foreach (var w in windows.Take(MaxWindows))
        {
            var o = new JsonObject { ["task"] = w.Task, ["maximized"] = w.Maximized };
            if (w.Desktop is { } d) o["desktop"] = d.ToString("D");
            if (w.Placement is { } p)
            {
                o["bounds"] = WriteRect(p.Bounds);
                o["monitor"] = WriteRect(p.Monitor);
                o["workArea"] = WriteRect(p.WorkArea);
                o["scale"] = p.Scale;
            }
            list.Add((JsonNode)o);
        }
        try
        {
            AtomicFile.Write(path, new JsonObject { ["windows"] = list }.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static SavedWindow? ReadWindow(JsonObject o)
    {
        var task = o["task"] is JsonValue t && t.TryGetValue<string>(out var s) && s.Length is > 0 and <= 64 ? s : null;
        var desktop = o["desktop"] is JsonValue dv && dv.TryGetValue<string>(out var ds) && Guid.TryParse(ds, out var g) && g != Guid.Empty ? g : (Guid?)null;
        var maximized = o["maximized"] is JsonValue mv && mv.TryGetValue<bool>(out var m) && m;
        SavedPlacement? placement = null;
        if (ReadRect(o["bounds"]) is { } bounds && ReadRect(o["monitor"]) is { } monitor && ReadRect(o["workArea"]) is { } work
            && o["scale"] is JsonValue sv && sv.TryGetValue<double>(out var scale) && scale is >= 0.5 and <= 8)
            placement = new SavedPlacement(bounds, monitor, work, scale);
        return new SavedWindow(task, desktop, placement, maximized);
    }

    private static PixelRect? ReadRect(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        int? I(string key) => o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
        if (I("x") is not { } x || I("y") is not { } y || I("width") is not { } w || I("height") is not { } h) return null;
        return w > 0 && h > 0 ? new PixelRect(x, y, w, h) : null;
    }

    private static JsonObject WriteRect(PixelRect r) =>
        new() { ["x"] = r.X, ["y"] = r.Y, ["width"] = r.Width, ["height"] = r.Height };
}

/// <summary>Where a saved window goes on the monitors there are now.</summary>
public static class WindowRestore
{
    /// <summary>
    /// The same monitor, unchanged: exactly where it was, kept inside the work area. A monitor that changed (or one
    /// now standing where it was): the same distance from the nearest corner, scaled. Its monitor gone, with nothing
    /// in its place: the primary. Null when there are no screens.
    /// </summary>
    public static PixelRect? Place(SavedPlacement saved, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0) return null;
        var b = saved.Bounds;

        var same = screens.FirstOrDefault(s => s.Bounds == saved.Monitor && s.WorkArea == saved.WorkArea && Math.Abs(s.Scale - saved.Scale) < 0.01);
        if (same is not null) return Fit(b.X, b.Y, b.Width, b.Height, same.WorkArea);

        var index = PlacementMath.PickMostOverlapping(screens.Select(s => s.Bounds.Tuple).ToList(), b.X, b.Y, b.Width, b.Height);
        var target = PlacementMath.OverlapArea(screens[index].Bounds.Tuple, b.X, b.Y, b.Width, b.Height) > 0
            ? screens[index]
            : screens.FirstOrDefault(s => s.Primary) ?? screens[0];

        var w = (int)Math.Round(b.Width / saved.Scale * target.Scale);
        var h = (int)Math.Round(b.Height / saved.Scale * target.Scale);
        w = Math.Max(1, Math.Min(w, target.WorkArea.Width));
        h = Math.Max(1, Math.Min(h, target.WorkArea.Height));
        var sw = saved.WorkArea;
        var placement = PlacementMath.FromPosition(b.X, b.Y, sw.X, sw.Y, sw.Width, sw.Height, saved.Scale, b.Width, b.Height);
        var tw = target.WorkArea;
        var (x, y) = PlacementMath.ToPosition(placement, tw.X, tw.Y, tw.Width, tw.Height, target.Scale, w, h);
        return new PixelRect(x, y, w, h);
    }

    private static PixelRect Fit(int x, int y, int w, int h, PixelRect work)
    {
        w = Math.Max(1, Math.Min(w, work.Width));
        h = Math.Max(1, Math.Min(h, work.Height));
        (x, y) = PlacementMath.Clamp(x, y, work.X, work.Y, work.Width, work.Height, w, h);
        return new PixelRect(x, y, w, h);
    }
}
