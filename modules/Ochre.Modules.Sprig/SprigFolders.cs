namespace Ochre.Modules.Sprig;

/// <summary>
/// Which sprig workspace a task folder (one of its repos or worktrees) is in, so the task can show that workspace's
/// apps. The deepest directory wins. Free slots never match: their worktrees sit on a detached HEAD waiting for the
/// next branch, so nothing of the task is there.
/// </summary>
internal static class SprigFolders
{
    public static SprigWorkspace? WorkspaceOf(SprigStatus status, string? folder)
    {
        if (!status.Seen || Normalize(folder) is not { } p) return null;
        SprigWorkspace? best = null;
        var bestLength = -1;
        foreach (var w in status.Workspaces)
        {
            if (w.IsFree) continue;
            foreach (var raw in w.Dirs)
            {
                if (Normalize(raw) is not { } dir || dir.Length <= bestLength || !IsUnder(p, dir)) continue;
                best = w;
                bestLength = dir.Length;
            }
        }
        return best;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="dir"/> or inside it, on a separator boundary (so
    /// <c>shop--10</c> isn't inside <c>shop--1</c>). Both are already normalized.</summary>
    internal static bool IsUnder(string path, string dir)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(dir, cmp)) return false;
        return path.Length == dir.Length || path[dir.Length] == Path.DirectorySeparatorChar;
    }

    internal static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            // A drive or filesystem root keeps its separator; anything else loses a trailing one.
            return full.Length > (Path.GetPathRoot(full)?.Length ?? 0) ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
