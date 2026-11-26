namespace Ochre.Core.Layout;

public enum HAnchor
{
    Left,
    Right,
}

public enum VAnchor
{
    Top,
    Bottom,
}

/// <summary>A window's top-left as DIP offsets from the nearest work-area corner, so it keeps its place when the
/// monitor's resolution or scale changes.</summary>
public sealed record Placement(HAnchor HAnchor, VAnchor VAnchor, double OffsetX, double OffsetY);

/// <summary>
/// Pure geometry for turning a <see cref="Placement"/> into a physical-pixel window position and back. Work areas,
/// positions and window sizes are physical pixels; placement offsets are DIP, converted with the monitor's scale
/// (physical = DIP x scale).
/// <para>
/// Horizontally the window's near edge is anchored (left to left, right to right). Vertically the top edge (the title
/// bar) is anchored to the nearest horizontal work-area edge, whatever the window's height, so the title bar stays
/// where it was and reachable.
/// </para>
/// </summary>
public static class PlacementMath
{
    /// <summary>Anchor and DIP offset to the clamped physical top-left of the window.</summary>
    public static (int X, int Y) ToPosition(
        Placement p, int waX, int waY, int waW, int waH, double scale, int physW, int physH)
    {
        var offX = (int)Math.Round(p.OffsetX * scale);
        var offY = (int)Math.Round(p.OffsetY * scale);

        var x = p.HAnchor == HAnchor.Left ? waX + offX : waX + waW - physW - offX;
        var y = p.VAnchor == VAnchor.Top ? waY + offY : waY + waH - offY;

        return Clamp(x, y, waX, waY, waW, waH, physW, physH);
    }

    /// <summary>
    /// Physical top-left to a <see cref="Placement"/> anchored to the nearest corner, with DIP offsets from those two
    /// edges. The position is clamped on-screen first, so the offsets are never negative.
    /// </summary>
    public static Placement FromPosition(
        int x, int y, int waX, int waY, int waW, int waH, double scale, int physW, int physH)
    {
        (x, y) = Clamp(x, y, waX, waY, waW, waH, physW, physH);

        var distLeft = x - waX;
        var distRight = waX + waW - (x + physW);
        // Both vertical distances are to the top edge, so the choice is "is the title bar in the top or bottom half?".
        var distTop = y - waY;
        var distBottom = waY + waH - y;

        var hAnchor = distLeft <= distRight ? HAnchor.Left : HAnchor.Right;
        var vAnchor = distTop <= distBottom ? VAnchor.Top : VAnchor.Bottom;

        var offXphys = hAnchor == HAnchor.Left ? distLeft : distRight;
        var offYphys = vAnchor == VAnchor.Top ? distTop : distBottom;

        return new Placement(hAnchor, vAnchor, Math.Max(0, offXphys) / scale, Math.Max(0, offYphys) / scale);
    }

    /// <summary>
    /// The screen a window rect belongs to: the one it overlaps most, or the nearest by centre distance when it
    /// overlaps none. -1 only for an empty list. Screens and the rect are physical pixels.
    /// <para>
    /// Overlap tolerates a resolution change on the same monitor, where the bounds differ but the window still sits
    /// over it, which exact-bounds matching cannot.
    /// </para>
    /// </summary>
    public static int PickMostOverlapping(
        IReadOnlyList<(int X, int Y, int W, int H)> screens, int rx, int ry, int rw, int rh)
    {
        var best = -1;
        long bestArea = 0;
        for (var i = 0; i < screens.Count; i++)
        {
            var area = OverlapArea(screens[i], rx, ry, rw, rh);
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }
        if (best >= 0 || screens.Count == 0) return best;

        long rcx = rx + rw / 2, rcy = ry + rh / 2;
        var bestDist = long.MaxValue;
        for (var i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            long dx = s.X + s.W / 2 - rcx, dy = s.Y + s.H / 2 - rcy;
            var dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    public static long OverlapArea((int X, int Y, int W, int H) s, int rx, int ry, int rw, int rh)
    {
        var ox = Math.Max(0L, Math.Min((long)rx + rw, (long)s.X + s.W) - Math.Max(rx, s.X));
        var oy = Math.Max(0L, Math.Min((long)ry + rh, (long)s.Y + s.H) - Math.Max(ry, s.Y));
        return ox * oy;
    }

    /// <summary>Keeps the window inside the work area; one too big for it pins to the top-left.</summary>
    public static (int X, int Y) Clamp(
        int x, int y, int waX, int waY, int waW, int waH, int physW, int physH)
    {
        // Upper bound first, then lower, so a window larger than the work area lands flush at waX/waY.
        x = Math.Min(x, waX + waW - physW);
        x = Math.Max(x, waX);
        y = Math.Min(y, waY + waH - physH);
        y = Math.Max(y, waY);
        return (x, y);
    }
}
