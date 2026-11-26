using Ochre.Core.Tasks;

namespace Ochre.Core.Files;

/// <summary>
/// Where the renderer may read or write a file (ADR 0015): inside one of a task's folders, named by a plain relative
/// path. The renderer is untrusted, so anything that could step outside the folder is refused rather than resolved:
/// rooted paths, <c>..</c>, drive and stream syntax, device names, and any link inside the folder. Nothing inside a
/// <c>.git</c> folder is reachable, since a write there (a hook) would run code on the next git command.
/// </summary>
internal static class TaskPaths
{
    private const int MaxRelativeLength = 1024;

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] Invalid = [.. Path.GetInvalidFileNameChars().Concat([':', '*', '?', '"', '<', '>', '|'])];

    /// <summary>The task's folders: its repos, then its worktrees.</summary>
    public static IReadOnlyList<string> Folders(TaskDocument doc) =>
        doc.Repos.Concat(doc.Worktrees.Select(w => w.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The task's own spelling of <paramref name="folder"/>, or null when the task doesn't record it.</summary>
    public static string? FolderOf(TaskDocument doc, string folder) =>
        Folders(doc).FirstOrDefault(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The full path of <paramref name="relative"/> inside <paramref name="folder"/>, or an error saying why not. Each
    /// existing step is checked by its real name on disk, so an 8.3 short name can't stand in for <c>.git</c>. The file
    /// itself may not exist (a deleted file's diff), but no step may be a link.
    /// </summary>
    public static bool TryResolve(string folder, string relative, out string fullPath, out string error)
    {
        fullPath = "";
        if (!TrySplit(relative, out var segments, out error)) return false;
        if (!Directory.Exists(folder))
        {
            error = "the folder isn't there";
            return false;
        }

        var current = Path.GetFullPath(folder);
        foreach (var segment in segments)
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo? entry;
            try
            {
                var dir = new DirectoryInfo(current);
                entry = dir.Exists ? dir.EnumerateFileSystemInfos(segment).FirstOrDefault() : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                error = "that path can't be read";
                return false;
            }

            if (entry is not null)
            {
                if (IsGitFolder(entry.Name))
                {
                    error = "files inside .git can't be opened here";
                    return false;
                }
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    error = "that path goes through a link";
                    return false;
                }
                next = Path.Combine(current, entry.Name);
            }
            else if (File.Exists(next) || Directory.Exists(next))
            {
                // Something answers to the name without listing under it: an 8.3 short name, which could stand for .git.
                error = "use the file's full name";
                return false;
            }
            current = next;
        }

        fullPath = current;
        return true;
    }

    /// <summary>
    /// The task folder holding the absolute <paramref name="path"/> (the deepest, when worktrees nest inside a repo)
    /// and the path relative to it with forward slashes; null when it's in none of them.
    /// </summary>
    public static (string Folder, string Relative)? Locate(TaskDocument doc, string path)
    {
        string full;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        foreach (var folder in Folders(doc).OrderByDescending(f => f.Length))
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (full.Length <= root.Length + 1 || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (full[root.Length] != Path.DirectorySeparatorChar) continue;
            var relative = full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
            return TrySplit(relative, out _, out _) ? (folder, relative) : null;
        }
        return null;
    }

    /// <summary>Splits a relative path into its names, refusing anything that isn't a plain path below the folder.</summary>
    internal static bool TrySplit(string relative, out string[] segments, out string error)
    {
        segments = [];
        error = "";
        if (string.IsNullOrEmpty(relative) || relative.Length > MaxRelativeLength)
        {
            error = "not a path";
            return false;
        }
        if (relative[0] is '/' or '\\' || Path.IsPathRooted(relative))
        {
            error = "the path must be relative to the folder";
            return false;
        }

        segments = relative.Split('/', '\\');
        foreach (var s in segments)
        {
            if (s.Length == 0 || s == "." || s == "..")
            {
                error = "the path must stay inside the folder";
                return false;
            }
            // Windows drops a trailing dot or space, so "a." would name the file "a".
            if (s.IndexOfAny(Invalid) >= 0 || s.Any(char.IsControl) || s[^1] is '.' or ' ')
            {
                error = "the path has a name Windows can't use";
                return false;
            }
            var stem = s.Split('.')[0].TrimEnd(' ');
            if (DeviceNames.Contains(stem))
            {
                error = "the path names a device";
                return false;
            }
            if (IsGitFolder(s))
            {
                error = "files inside .git can't be opened here";
                return false;
            }
        }
        return true;
    }

    private static bool IsGitFolder(string name) => string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase);
}
