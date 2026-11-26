namespace Ochre.Core.Git.Graph;

/// <summary>
/// Loads a <see cref="GraphSnapshot"/> for the worktree holding a folder: refs, every worktree's HEAD and change
/// count, the first page of the scope's log, and the lane layout. Blocking file and process IO: call it off the UI
/// thread. Best-effort: null when the folder isn't in a repo.
/// </summary>
internal static class GraphLoader
{
    public const int PageSize = 2000;

    // Tried in order for the trunk, local before remote.
    private static readonly string[] TrunkNames = ["main", "master", "trunk", "develop"];

    /// <param name="baseOverride">For <see cref="GraphScope.Branch"/>: the base ref to scope against; null picks the best
    /// candidate, and an empty string shows HEAD's history unscoped.</param>
    public static GraphSnapshot? Load(string cwd, GraphScope scope, int pageSize = PageSize, string? baseOverride = null)
    {
        if (GitCheckoutScanner.FindRoot(cwd) is not { } root)
            return null;

        // Starting git dominates on Windows (~100 ms a process under endpoint protection), so the refs, every
        // worktree's status and the log run side by side: the query names branches rather than resolved hashes, so
        // none of them waits on another.
        var trees = ScanWorktrees(root);
        var refsTask = Task.Run(() => GraphLogReader.LoadRefs(root));
        var statusTask = Task.Run(() => ReadStatuses(trees));
        var stashTask = Task.Run(() => GraphLogReader.LoadStashes(root));
        // Only the log needs the scope's query, and the Branch scope runs git to build it (base candidates and the
        // merge base), so the rest is already under way meanwhile.
        var (query, baseRef, candidates) = QueryFor(scope, root, trees, baseOverride);
        var logTask = Task.Run(() => GraphLogReader.ReadLog(root, pageSize, 0, query));
        Task.WaitAll(refsTask, logTask, statusTask, stashTask);
        var refs = refsTask.Result;
        var worktrees = Resolve(trees, statusTask.Result, refs);
        var log = logTask.Result is { } raw ? GraphLogReader.ParseLog(raw) : [];
        return GraphSnapshot.Build(root, scope, query, log, refs, worktrees, PrimaryBranch(refs), pageSize,
            new GraphSnapshot.ScopeContext(baseRef, candidates), stashTask.Result);
    }

    /// <summary>The next page of <paramref name="s"/>'s query laid out below it, or <paramref name="s"/> itself when
    /// there's nothing more.</summary>
    public static GraphSnapshot LoadMore(GraphSnapshot s, int pageSize = PageSize)
    {
        if (!s.HasMore)
            return s;
        var page = GraphLogReader.LoadLog(s.Root, pageSize, s.RealCommitCount, s.Query);
        return s.Append(page, pageSize);
    }

    /// <summary>The trunk: the first trunk name that exists locally, else on a remote. Lane 0 when the graph has no
    /// worktree of its own (see <see cref="GraphSnapshot.PrimaryTip"/>).</summary>
    internal static string? PrimaryBranch(IReadOnlyList<GraphRef> refs)
    {
        foreach (var t in TrunkNames)
            if (refs.Any(r => r.Kind == GraphRefKind.LocalBranch && r.Name == t))
                return t;
        foreach (var t in TrunkNames)
            if (refs.FirstOrDefault(r => r.Kind == GraphRefKind.RemoteBranch && r.Name.EndsWith("/" + t, StringComparison.Ordinal)) is { } r)
                return r.Name;
        return null;
    }

    /// <summary>A worktree as read from disk, before git has run: its branch, or its commit when detached.</summary>
    internal readonly record struct ScannedWorktree(string Path, string? Branch, string? DetachedHead, bool IsCurrent);

    /// <summary>
    /// What each scope walks. Worktrees are named by branch (<c>refs/heads/...</c>) or detached hash, so the walk
    /// doesn't wait for the ref listing. Only <see cref="GraphScope.Branch"/> runs git (base ref and merge-base).
    /// </summary>
    internal static (GraphLogQuery Query, string? BaseRef, IReadOnlyList<string> Candidates) QueryFor(
        GraphScope scope, string root, IReadOnlyList<ScannedWorktree> trees, string? baseOverride = null)
    {
        switch (scope)
        {
            case GraphScope.Worktrees:
                var tips = trees.Select(t => t.Branch is not null ? "refs/heads/" + t.Branch : t.DetachedHead)
                    .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
                // Whichever trunk names exist (a pattern that matches nothing adds nothing).
                tips.AddRange(TrunkNames.Select(t => "--branches=" + t));
                return (new(tips, []), null, []);

            case GraphScope.Branch:
                var current = trees.FirstOrDefault(t => t.IsCurrent);
                var candidates = GitBaseRefs.Candidates(root, current.Branch);
                // On a trunk there's nothing to scope against: show its recent history.
                var baseRef = baseOverride is { Length: 0 } ? null
                    : baseOverride ?? (current.Branch is { } b && !TrunkNames.Contains(b) ? candidates.FirstOrDefault() : null);
                if (baseRef is null)
                    return (new(["HEAD"], []), null, candidates);
                var mergeBase = GitBaseRefs.MergeBase(root, baseRef);
                return (new(["HEAD", baseRef], mergeBase is null ? [] : [mergeBase + "^@"]), baseRef, candidates);

            default:
                // Detached worktree heads aren't reachable from any ref, so they're named explicitly.
                var detached = trees.Select(t => t.DetachedHead).OfType<string>().ToList();
                var all = detached.Count == 0 ? GraphLogQuery.Everything : new([.. GraphLogQuery.Everything.Tips, .. detached], []);
                return (all, null, []);
        }
    }

    /// <summary>Every worktree of the repo, read from the shared git dir with no git process.</summary>
    internal static IReadOnlyList<ScannedWorktree> ScanWorktrees(string root)
    {
        var set = GitWorktreeScanner.Read(root);
        List<GitWorktree> trees = set is null ? [new GitWorktree(root, null)]
            : set.Bare ? [.. set.Linked] : [new GitWorktree(set.Root, set.MainBranch), .. set.Linked];
        return trees.Select(t => new ScannedWorktree(t.Path, t.Branch, t.Branch is null ? DetachedHead(t.Path) : null,
            SamePath(t.Path, root))).ToList();
    }

    // One `git status` per worktree, in parallel through GitRunner's gate.
    private static GitRepoStatus?[] ReadStatuses(IReadOnlyList<ScannedWorktree> trees)
    {
        var result = new GitRepoStatus?[trees.Count];
        Parallel.For(0, trees.Count, i => result[i] = GitStatusReader.Read(trees[i].Path));
        return result;
    }

    internal static IReadOnlyList<GraphWorktree> Resolve(IReadOnlyList<ScannedWorktree> trees,
        IReadOnlyList<GitRepoStatus?> statuses, IReadOnlyList<GraphRef> refs) =>
        trees.Select((t, i) =>
        {
            var branch = t.Branch ?? (t.DetachedHead is null ? statuses[i]?.Branch : null);
            var head = branch is not null
                ? refs.FirstOrDefault(r => r.Kind == GraphRefKind.LocalBranch && r.Name == branch)?.Target
                : t.DetachedHead;
            return new GraphWorktree(t.Path, branch, head, statuses[i]?.Changes.Count ?? 0, t.IsCurrent);
        }).ToList();

    // A detached worktree's HEAD file holds the commit hash itself.
    internal static string? DetachedHead(string worktree)
    {
        try
        {
            if (GitDirOf(worktree) is not { } gitDir)
                return null;
            var head = File.ReadLines(Path.Combine(gitDir, "HEAD")).FirstOrDefault()?.Trim() ?? "";
            return head.Length >= 40 && head.All(Uri.IsHexDigit) ? head : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A worktree's own git dir: its <c>.git</c> folder (main worktree) or the per-worktree dir its
    /// <c>.git</c> file names (linked worktree). Null when there's neither.</summary>
    internal static string? GitDirOf(string worktree)
    {
        try
        {
            var dotGit = Path.Combine(worktree, ".git");
            if (Directory.Exists(dotGit)) return dotGit;
            return File.Exists(dotGit) && File.ReadLines(dotGit).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase)) is { } line
                ? Path.GetFullPath(Path.Combine(worktree, line["gitdir:".Length..].Trim()))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // Git records a linked worktree's path with its links resolved, while the graph's own path is as it was opened, so
    // equal-as-written falls back to the real paths.
    private static bool SamePath(string a, string b) => SameText(a, b) || SameText(RealPath(a), RealPath(b));

    private static bool SameText(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The path with a link at any level resolved, not only the last one (where
    /// <see cref="Directory.ResolveLinkTarget"/> alone stops). A part that can't be resolved is kept as written.</summary>
    internal static string RealPath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
        var current = Path.GetPathRoot(full) ?? "";
        foreach (var part in full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if (Directory.ResolveLinkTarget(current, returnFinalTarget: true) is { } target)
                    current = target.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Missing or unreadable: compare it as written.
            }
        }
        return current;
    }
}
