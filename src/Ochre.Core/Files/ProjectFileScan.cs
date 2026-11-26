using Ochre.Core.Git;

namespace Ochre.Core.Files;

/// <summary>All of a folder's files, as forward-slashed paths relative to it, sorted and de-duplicated.
/// <see cref="Truncated"/> is true when the cap was hit, so the caller can say the list isn't complete.</summary>
internal sealed record ProjectFiles(IReadOnlyList<string> RelativePaths, bool Truncated, bool Git)
{
    public static readonly ProjectFiles Empty = new([], false, false);
}

/// <summary>
/// Lists a task folder's files for the editor's tree and Ctrl+P. In a git checkout it runs <c>git ls-files --cached
/// --others --exclude-standard</c>, so <c>.gitignore</c> is honoured; outside one it walks a bounded depth, skipping
/// the usual build and package folders and never following links. Best-effort and off the UI thread: git missing, a
/// timeout or an unreadable folder give <see cref="ProjectFiles.Empty"/> rather than throwing.
/// </summary>
internal static class ProjectFileScan
{
    private const int GitTimeoutMs = 8000;
    private const int MaxDepth = 8;
    public const int MaxFiles = 20_000;

    private static readonly string[] SkipDirs =
        [".git", "node_modules", "bin", "obj", ".venv", "venv", "dist", "build", ".next", "target", ".idea", ".vs", ".gradle"];

    public static ProjectFiles Scan(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return ProjectFiles.Empty;

        if (GitCheckoutScanner.FindRoot(folder) is not null && FromGit(folder) is { } gitFiles)
            return gitFiles;

        return FromWalk(folder);
    }

    private static ProjectFiles? FromGit(string folder)
    {
        // Paths come back relative to the folder git runs in, which is what the editor wants; -z keeps odd names whole.
        var r = GitRunner.Run(folder, GitTimeoutMs, "--no-optional-locks", "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        if (r.Exit != 0)
            return null;
        return Finish(r.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Replace('\\', '/')), truncated: false, git: true);
    }

    private static ProjectFiles FromWalk(string folder)
    {
        var results = new List<string>();
        var truncated = false;
        var stack = new Stack<(DirectoryInfo Dir, int Depth)>();
        stack.Push((new DirectoryInfo(folder), 0));

        while (stack.Count > 0 && !truncated)
        {
            var (dir, depth) = stack.Pop();

            FileInfo[] files;
            try
            {
                files = dir.GetFiles();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            foreach (var f in files)
            {
                results.Add(Path.GetRelativePath(folder, f.FullName).Replace('\\', '/'));
                if (results.Count >= MaxFiles)
                {
                    truncated = true;
                    break;
                }
            }
            if (truncated || depth >= MaxDepth)
                continue;

            DirectoryInfo[] subs;
            try
            {
                subs = dir.GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            foreach (var sub in subs)
            {
                if (SkipDirs.Contains(sub.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                // Links can loop or lead out of the folder.
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                stack.Push((sub, depth + 1));
            }
        }

        return Finish(results, truncated, git: false);
    }

    private static ProjectFiles Finish(IEnumerable<string> paths, bool truncated, bool git)
    {
        var list = paths
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count > MaxFiles)
        {
            list = list.Take(MaxFiles).ToList();
            truncated = true;
        }
        return new ProjectFiles(list, truncated, git);
    }
}
