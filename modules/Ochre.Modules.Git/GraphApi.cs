using System.Text.RegularExpressions;
using Ochre.Core.Bridge;
using Ochre.Core.Git;
using Ochre.Core.Git.Graph;
using Ochre.Core.Tasks;
using Ochre.Modules.Git.Bridge;

namespace Ochre.Modules.Git;

/// <summary>
/// The git graph's bridge messages. A graph is only ever loaded for one of a task's own folders; everything after
/// that names the load's token, so git runs only in that checkout, on commits the graph showed and worktrees it
/// listed. Read-only: nothing here changes a repo.
/// </summary>
internal static partial class GraphApi
{
    private const int CommitTimeoutMs = 6000;

    public static void Register(BridgeDispatcher bridge, TaskStore tasks, GraphLoads loads)
    {
        var j = GitJson.Default;

        bridge.Handle("graph.load", j.GraphLoadRequest, j.GraphPageDto, async r =>
        {
            if (!TaskStore.IsTaskId(r.TaskId)) throw new BridgeRejectException("invalid task id");
            var doc = tasks.Get(r.TaskId) ?? throw new BridgeRejectException("no such task");
            var folder = doc.Repos.Concat(doc.Worktrees.Select(w => w.Path))
                .FirstOrDefault(f => string.Equals(f, r.Folder, StringComparison.OrdinalIgnoreCase))
                ?? throw new BridgeRejectException("not one of the task's folders");
            return await Task.Run(() => Load(loads, folder, r.Scope, r.Base)).ConfigureAwait(false);
        });

        bridge.Handle("graph.more", j.GraphTokenRequest, j.GraphMoreDto, async r =>
        {
            var load = loads.Get(r.Token);
            return await Task.Run(() => load.More()).ConfigureAwait(false);
        });

        bridge.Handle("graph.commit", j.GraphCommitRequest, j.GraphCommitDto, async r =>
        {
            var snapshot = loads.Get(r.Token).Snapshot;
            // A full hash the graph showed, so git is never handed an option or a ref the renderer made up.
            if (!FullHash().IsMatch(r.Hash) || snapshot.RowOf(r.Hash) < 0) throw new BridgeRejectException("not a commit in this graph");
            return await Task.Run(() => Commit(snapshot.Root, r.Hash)).ConfigureAwait(false)
                ?? throw new BridgeRejectException("git couldn't read that commit");
        });

        bridge.Handle("graph.changes", j.GraphChangesRequest, j.IReadOnlyListGraphChangeDto, async r =>
        {
            var snapshot = loads.Get(r.Token).Snapshot;
            var worktree = snapshot.Worktrees.FirstOrDefault(w => string.Equals(w.Path, r.Path, StringComparison.Ordinal))
                ?? throw new BridgeRejectException("not a worktree in this graph");
            return (IReadOnlyList<GraphChangeDto>)await Task.Run(() => Changes(worktree.Path)).ConfigureAwait(false);
        });
    }

    private static GraphPageDto Load(GraphLoads loads, string folder, GraphScopeDto scopeDto, string? baseRef)
    {
        var scope = scopeDto switch
        {
            GraphScopeDto.Worktrees => GraphScope.Worktrees,
            GraphScopeDto.Branch => GraphScope.Branch,
            _ => GraphScope.All,
        };
        if (!Directory.Exists(folder)) return Empty(scopeDto, folder, "This folder isn't there any more.");
        // A base the user picked must be one git listed: it goes on git's command line.
        if (scope == GraphScope.Branch && baseRef is { Length: > 0 } && !GitBaseRefs.Candidates(folder, null).Contains(baseRef, StringComparer.Ordinal))
            throw new BridgeRejectException("not a base branch of this checkout");
        if (GraphLoader.Load(folder, scope, baseOverride: scope == GraphScope.Branch ? baseRef : null) is not { } snapshot)
            return Empty(scopeDto, folder, "This folder isn't in a git checkout.");

        var token = loads.Add(snapshot);
        var (ahead, behind) = snapshot.Context.BaseRef is { } b ? AheadBehind(snapshot.Root, b) : (null, null);
        return new GraphPageDto(
            token,
            snapshot.Root,
            scopeDto,
            snapshot.Context.BaseRef,
            snapshot.Context.BaseCandidates,
            ahead,
            behind,
            Rows(snapshot, 0),
            snapshot.Refs.Select(r => new GraphRefDto(r.Name, Kind(r.Kind), r.Target, r.Upstream, r.Ahead, r.Behind, r.UpstreamGone)).ToList(),
            snapshot.Worktrees.Select(w => new GraphWorktreeDto(w.Path, w.Name, w.Branch, w.Head, w.Changes, w.IsCurrent)).ToList(),
            snapshot.Stashes.Select(s => new GraphStashDto(s.Selector, s.Text, s.Message, s.RowHash, s.Base, s.UnixTime)).ToList(),
            snapshot.Width,
            snapshot.HasMore,
            OpenLanes(snapshot),
            null);
    }

    private static GraphPageDto Empty(GraphScopeDto scope, string folder, string error) =>
        new("", folder, scope, null, [], null, null, [], [], [], [], 0, false, [], error);

    // The base came from git's own ref list (the loader's candidates), so it's safe on the command line.
    private static (int?, int?) AheadBehind(string root, string baseRef)
    {
        var r = GitRunner.Run(root, CommitTimeoutMs, "--no-optional-locks", "rev-list", "--left-right", "--count", $"HEAD...{baseRef}");
        var f = r.Stdout.Split(['\t', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return r.Exit == 0 && f.Length == 2 && int.TryParse(f[0], out var a) && int.TryParse(f[1], out var b) ? (a, b) : (null, null);
    }

    internal static List<GraphRowDto> Rows(GraphSnapshot s, int from)
    {
        var rows = new List<GraphRowDto>(Math.Max(0, s.Commits.Count - from));
        for (var i = from; i < s.Commits.Count; i++)
        {
            var c = s.Commits[i];
            var row = s.Rows[i];
            var up = new List<int>(row.Up.Length * 4);
            foreach (var e in row.Up)
            {
                up.Add(e.From);
                up.Add(e.To);
                up.Add(e.Color);
                up.Add(s.IsTether(i, e) ? 1 : 0);
            }
            var kind = c.IsWorkInProgress ? GraphRowKindDto.Wip : c.IsStash ? GraphRowKindDto.Stash : GraphRowKindDto.Commit;
            rows.Add(new GraphRowDto(c.Hash, kind, c.Subject, c.AuthorName, c.UnixTime, row.Lane, c.IsMerge, up));
        }
        return rows;
    }

    internal static List<int> OpenLanes(GraphSnapshot s) => s.OpenEdges.Select(e => (int)e.To).Distinct().ToList();

    private static GraphRefKindDto Kind(GraphRefKind k) => k switch
    {
        GraphRefKind.RemoteBranch => GraphRefKindDto.Remote,
        GraphRefKind.Tag => GraphRefKindDto.Tag,
        GraphRefKind.Stash => GraphRefKindDto.Stash,
        _ => GraphRefKindDto.Local,
    };

    private static GraphCommitDto? Commit(string root, string hash)
    {
        var r = GitRunner.Run(root, CommitTimeoutMs, "--no-optional-locks", "show", "-s", "--format=%H%x1f%an%x1f%ae%x1f%at%x1f%P%x1f%B", hash);
        return r.Exit == 0 ? ParseCommit(r.Stdout) : null;
    }

    /// <summary>Parses <c>git show -s</c> in the format <see cref="Commit"/> asks for. Null when it doesn't fit.</summary>
    internal static GraphCommitDto? ParseCommit(string output)
    {
        var f = output.Split('\u001f', 6);
        if (f.Length < 6 || f[0].Trim().Length < 7) return null;
        var time = long.TryParse(f[3], out var t) ? t : 0;
        var parents = f[4].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new GraphCommitDto(f[0].Trim(), f[1], f[2], time, parents, f[5].Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    private static List<GraphChangeDto> Changes(string worktree)
    {
        if (GitStatusReader.Read(worktree) is not { } status) return [];
        return status.Changes.Select(c => new GraphChangeDto(c.Path, Letter(c))).ToList();
    }

    private static string Letter(GitFileChange c)
    {
        if (c.Untracked) return "?";
        var kind = c.Unstaged != GitChangeKind.None ? c.Unstaged : c.Staged;
        return kind switch
        {
            GitChangeKind.Added => "A",
            GitChangeKind.Deleted => "D",
            GitChangeKind.Renamed => "R",
            GitChangeKind.Copied => "C",
            GitChangeKind.TypeChanged => "T",
            GitChangeKind.Unmerged => "U",
            _ => "M",
        };
    }

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$")]
    private static partial Regex FullHash();
}

/// <summary>
/// The graphs windows have loaded, by token, so the next page lays out below the last one and details are read
/// from the same checkout. Keeps the most recent few; a window holding an older token is asked to reload.
/// </summary>
internal sealed class GraphLoads
{
    private const int Max = 16;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, GraphLoad> _loads = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public string Add(GraphSnapshot snapshot)
    {
        var token = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _loads[token] = new GraphLoad(snapshot);
            _order.Enqueue(token);
            while (_order.Count > Max) _loads.Remove(_order.Dequeue());
        }
        return token;
    }

    public GraphLoad Get(string token)
    {
        lock (_gate)
            return _loads.TryGetValue(token, out var load) ? load : throw new BridgeRejectException("this graph has expired; refresh it");
    }
}

internal sealed class GraphLoad(GraphSnapshot snapshot)
{
    private readonly Lock _gate = new();
    private GraphSnapshot _snapshot = snapshot;

    public GraphSnapshot Snapshot
    {
        get
        {
            lock (_gate) return _snapshot;
        }
    }

    /// <summary>The next page; one at a time, since a snapshot can be appended to only once.</summary>
    public GraphMoreDto More()
    {
        lock (_gate)
        {
            var before = _snapshot.Commits.Count;
            _snapshot = GraphLoader.LoadMore(_snapshot);
            return new GraphMoreDto(GraphApi.Rows(_snapshot, before), _snapshot.Width, _snapshot.HasMore, GraphApi.OpenLanes(_snapshot));
        }
    }
}
