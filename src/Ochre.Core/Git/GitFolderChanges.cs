using Ochre.Core.Files;

namespace Ochre.Core.Git;

/// <param name="Path">Relative to the task folder, forward slashes.</param>
/// <param name="OrigPath">A rename's old path, relative to the folder; null otherwise (or when it was outside the folder).</param>
/// <param name="RootPath">Relative to the repo root, as git names it at a commit.</param>
/// <param name="OrigRootPath">A rename's old path relative to the repo root.</param>
/// <param name="Letter">As <see cref="GitStatusReader.Letter"/> gives it.</param>
/// <param name="Added">Lines added since HEAD; null when unknown (binary, too big to count, or no HEAD yet).</param>
internal sealed record GitFolderChange(
    string Path,
    string? OrigPath,
    string RootPath,
    string? OrigRootPath,
    string Letter,
    bool Untracked,
    int? Added,
    int? Removed,
    bool Binary);

/// <param name="Root">The repo root the folder is in.</param>
internal sealed record GitFolderChangeList(string Root, IReadOnlyList<GitFolderChange> Changes, bool Truncated);

/// <summary>
/// What changed in a task folder since HEAD, with line counts, for the editor's Changes list (M6), and the text of a
/// file at a commit for a diff's left side. A folder can be a subfolder of its repo, so only changes under it are kept,
/// with paths made relative to it.
/// </summary>
internal static class GitFolderChanges
{
    private const int TimeoutMs = 8000;
    public const int MaxChanges = 2000;
    private const int MaxUntrackedCounts = 500;
    private const long MaxUntrackedBytes = 1024 * 1024;

    /// <summary>The folder's changes, or null when it isn't in a git checkout git can read.</summary>
    public static GitFolderChangeList? Read(string folder)
    {
        if (GitCheckoutScanner.FindRoot(folder) is not { } root) return null;
        // Every file of an untracked folder, so each can be opened; the cap below keeps a stray build folder in check.
        var status = GitRunner.Run(folder, TimeoutMs, "--no-optional-locks", "status", "--porcelain=v2", "-z", "--untracked-files=all");
        if (status.Exit != 0) return null;

        var prefix = Prefix(root, folder);
        var counts = Numstat(folder);
        var list = new List<GitFolderChange>();
        var untrackedCounted = 0;
        foreach (var c in GitStatusReader.ParseStatusV2Z(status.Stdout).Changes)
        {
            if (Under(prefix, c.Path) is not { } path) continue;
            if (list.Count == MaxChanges) return new GitFolderChangeList(root, list, true);

            int? added = null, removed = null;
            var binary = false;
            if (c.Untracked)
            {
                if (untrackedCounted < MaxUntrackedCounts && CountLines(Path.Combine(folder, path)) is { } n)
                {
                    untrackedCounted++;
                    binary = n.Binary;
                    if (!binary) (added, removed) = (n.Lines, 0);
                }
            }
            else if (counts.TryGetValue(c.Path, out var stat))
            {
                (added, removed, binary) = stat;
            }
            list.Add(new GitFolderChange(path, c.OrigPath is { } o ? Under(prefix, o) : null, c.Path, c.OrigPath, GitStatusReader.Letter(c), c.Untracked, added, removed, binary));
        }
        return new GitFolderChangeList(root, list, false);
    }

    /// <summary>
    /// A file's text at <paramref name="rev"/> (a commit hash or <c>HEAD</c>, never from the page), read raw with
    /// <c>cat-file</c> so no filter or textconv runs. Missing when the path isn't in that commit.
    /// </summary>
    public static TextFileRead ReadAt(string root, string rev, string rootPath)
    {
        var spec = $"{rev}:{rootPath}";
        var size = GitRunner.Run(root, TimeoutMs, "--no-optional-locks", "cat-file", "-s", spec);
        if (size.Exit != 0 || !long.TryParse(size.Stdout.Trim(), out var bytes))
            return new TextFileRead(null, "", 0, false, TextFileProblem.Missing, true);
        if (bytes > TextFiles.MaxReadBytes)
            return new TextFileRead(null, "", bytes, false, TextFileProblem.TooLarge, true);

        var blob = GitRunner.Run(root, TimeoutMs, "--no-optional-locks", "cat-file", "blob", spec);
        if (blob.Exit != 0) return new TextFileRead(null, "", 0, false, TextFileProblem.Missing, true);
        // Git's output is decoded as UTF-8 on the way in, so binary shows up as the NULs it kept.
        var text = blob.Stdout;
        if (text.AsSpan(0, Math.Min(text.Length, 8000)).Contains('\0'))
            return new TextFileRead(null, "", bytes, false, TextFileProblem.Binary, true);
        return new TextFileRead(text, "", bytes, false, TextFileProblem.None, true);
    }

    /// <summary>The folder's path inside its repo with a trailing slash, or empty for the root itself.</summary>
    internal static string Prefix(string root, string folder)
    {
        var rel = Path.GetRelativePath(root, folder).Replace('\\', '/');
        return rel is "." or "" ? "" : rel.TrimEnd('/') + "/";
    }

    /// <summary>A root-relative path made relative to the folder, or null when it's outside it.</summary>
    internal static string? Under(string prefix, string rootPath) =>
        prefix.Length == 0 ? rootPath
        : rootPath.Length > prefix.Length && rootPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rootPath[prefix.Length..]
        : null;

    // Tracked files' line counts against HEAD, keyed by root-relative path. Empty before the first commit.
    private static Dictionary<string, (int?, int?, bool)> Numstat(string folder)
    {
        var r = GitRunner.Run(folder, TimeoutMs, "--no-optional-locks", "diff", "--numstat", "-z", "--no-renames", "HEAD");
        return r.Exit == 0 ? ParseNumstatZ(r.Stdout) : new Dictionary<string, (int?, int?, bool)>(StringComparer.Ordinal);
    }

    /// <summary>Parses <c>git diff --numstat -z --no-renames</c>: <c>added\tremoved\tpath\0</c>, with <c>-\t-</c> for binary.</summary>
    internal static Dictionary<string, (int? Added, int? Removed, bool Binary)> ParseNumstatZ(string output)
    {
        var map = new Dictionary<string, (int?, int?, bool)>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = record.Split('\t', 3);
            if (f.Length < 3) continue;
            var binary = f[0] == "-" && f[1] == "-";
            map[f[2]] = binary
                ? (null, null, true)
                : (int.TryParse(f[0], out var a) ? a : null, int.TryParse(f[1], out var d) ? d : null, false);
        }
        return map;
    }

    // An untracked file's lines all count as added. Big files and links aren't read.
    private static (int Lines, bool Binary)? CountLines(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxUntrackedBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) return (0, true);
            var lines = bytes.AsSpan().Count((byte)'\n');
            return (bytes.Length > 0 && bytes[^1] != '\n' ? lines + 1 : lines, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
