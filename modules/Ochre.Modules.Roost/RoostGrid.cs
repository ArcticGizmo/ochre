namespace Ochre.Modules.Roost;

/// <summary>A grid as saved: its regions, the session in each, the zoomed region, and every session it has seen.</summary>
internal sealed record RoostGridState(IReadOnlyList<RoostRegion> Regions, IReadOnlyDictionary<int, string> Cells, int? Zoomed, IReadOnlyList<string> Known);

/// <summary>
/// One task's grid: a layout and the session in each region. Placement is the user's: nothing already placed ever
/// moves by itself, a session the user took out stays out, and only a session the grid has never seen is placed
/// for them, in an empty region. The rules for assigning, swapping, zooming and changing layout are Perch's
/// (<c>RoostTabSet</c>, 9275e0b) for a single tab. UI-free, unit-tested, not thread-safe.
/// </summary>
internal sealed class RoostGrid
{
    private readonly Dictionary<int, string> _cells = [];
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);

    private RoostGrid(RoostGridLayout layout) => Layout = layout;

    public RoostGridLayout Layout { get; private set; }

    /// <summary>Region id → session id, for the regions that hold one.</summary>
    public IReadOnlyDictionary<int, string> Cells => _cells;

    /// <summary>The region filling the grid for now, or null.</summary>
    public int? Zoomed { get; private set; }

    public string? At(int regionId) => _cells.TryGetValue(regionId, out var s) ? s : null;

    public int? RegionOf(string sessionId) => _cells.Where(c => c.Value == sessionId).Select(c => (int?)c.Key).FirstOrDefault();

    /// <summary>A new grid shaped for <paramref name="sessions"/> (up to <see cref="RoostTemplates.AutoMaxCells"/>) on a
    /// stage <paramref name="aspect"/> wide, filled with them in order.</summary>
    public static RoostGrid For(IReadOnlyList<string> sessions, double aspect)
    {
        var take = sessions.Take(RoostTemplates.AutoMaxCells).ToList();
        var grid = new RoostGrid(RoostGridLayout.FromTemplate(RoostTemplates.ForCount(Math.Max(1, take.Count), aspect)));
        var regions = grid.Layout.ReadingOrder;
        for (int i = 0; i < take.Count; i++) grid._cells[regions[i].Id] = take[i];
        grid._known.UnionWith(sessions);
        return grid;
    }

    /// <summary>A saved grid read back. An invalid layout reads as one region; cells naming a region it doesn't have,
    /// or a session twice, are dropped.</summary>
    public static RoostGrid FromState(RoostGridState state)
    {
        var grid = new RoostGrid(RoostGridLayout.FromPersisted(state.Regions));
        foreach (var (region, session) in state.Cells)
            if (grid.Layout.Find(region) is not null && !grid._cells.ContainsValue(session)) grid._cells[region] = session;
        grid.Zoomed = state.Zoomed is { } z && grid._cells.ContainsKey(z) ? z : null;
        grid._known.UnionWith(state.Known);
        grid._known.UnionWith(grid._cells.Values);
        return grid;
    }

    public RoostGridState ToState() =>
        new(Layout.Regions.ToList(), new Dictionary<int, string>(_cells), Zoomed, _known.Order(StringComparer.Ordinal).ToList());

    /// <summary>
    /// Puts <paramref name="sessionId"/> in a region. If it was in another region, the region's occupant takes that
    /// place (a swap); if it wasn't in the grid, the occupant leaves the grid. Returns the session that left, if any.
    /// </summary>
    public string? Assign(int regionId, string sessionId)
    {
        if (Layout.Find(regionId) is null) return null;
        var occupant = At(regionId);
        if (occupant == sessionId) return null;
        var from = RegionOf(sessionId);
        if (from is { } f) _cells.Remove(f);
        _cells[regionId] = sessionId;
        _known.Add(sessionId);
        if (occupant is null) return null;
        if (from is { } back)
        {
            _cells[back] = occupant;
            return null;
        }
        if (Zoomed == regionId) Zoomed = null;
        return occupant;
    }

    /// <summary>Takes a session out of the grid ("Remove from grid"). It stays out until the user puts it back.</summary>
    public bool Unassign(string sessionId)
    {
        if (RegionOf(sessionId) is not { } region) return false;
        _cells.Remove(region);
        if (Zoomed == region) Zoomed = null;
        return true;
    }

    /// <summary>Zooms a region to fill the grid, or (null, or the zoomed one again) restores it.</summary>
    public int? ToggleZoom(int? regionId)
    {
        Zoomed = regionId is { } r && Zoomed != r && Layout.Find(r) is not null ? r : null;
        return Zoomed;
    }

    /// <summary>
    /// Puts a preset on the grid. It takes over the current regions' ids in reading order, so sessions keep their
    /// reading position; sessions in regions the preset doesn't have leave the grid, and are returned.
    /// </summary>
    public IReadOnlyList<string> ApplyLayout(RoostGridLayout layout)
    {
        Layout = layout.AdoptIds(Layout);
        var dropped = _cells.Where(c => Layout.Find(c.Key) is null).ToList();
        foreach (var (region, _) in dropped) _cells.Remove(region);
        if (Zoomed is { } z && Layout.Find(z) is null) Zoomed = null;
        return dropped.Select(c => c.Value).ToList();
    }

    /// <summary>
    /// Follows the task's sessions: a session that's gone leaves its region empty, and a session the grid has never
    /// seen goes into the first empty region (reading order), if there is one. Returns whether anything changed.
    /// </summary>
    public bool Sync(IReadOnlyList<string> sessions)
    {
        var live = sessions.ToHashSet(StringComparer.Ordinal);
        bool changed = false;
        foreach (var (region, session) in _cells.Where(c => !live.Contains(c.Value)).ToList())
        {
            _cells.Remove(region);
            if (Zoomed == region) Zoomed = null;
            changed = true;
        }
        foreach (var session in sessions)
        {
            if (!_known.Add(session)) continue;
            changed = true;
            if (Layout.ReadingOrder.FirstOrDefault(r => !_cells.ContainsKey(r.Id)) is { Area: > 0 } empty) _cells[empty.Id] = session;
        }
        _known.IntersectWith(live);
        return changed;
    }
}
