using System.Collections.Concurrent;
using Ochre.Core.Bridge;
using Ochre.Core.Git;
using Ochre.Core.Modules;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Ochre.Modules.Git.Bridge;

namespace Ochre.Modules.Git;

/// <summary>
/// Git for tasks (M4 slice 2): each task folder's branch, changes and ahead/behind, and a <c>git-worktree</c>
/// provider so an agent or terminal can start in a fresh checkout on its own branch. Status is read when a window
/// asks (on focus) and after an agent's turn ends, never on a timer.
/// </summary>
internal sealed class GitModule : IModule
{
    public string Id => "git";
    public string Name => "Git";
    public string Description => "Shows each task folder's branch, changes and commits ahead or behind, and lets agents and terminals start in a new git worktree for the task.";
    public bool DefaultEnabled => true;

    public IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("git.status", typeof(GitStatusRequest), typeof(IReadOnlyList<GitFolderStatus>)),
    ];

    public IReadOnlyList<EventContract> Events { get; } =
    [
        new("git.changed", typeof(GitChangedEvent)),
    ];

    public IDisposable Start(IModuleContext context)
    {
        context.AddWorktreeProvider(new GitWorktreeProvider());
        var cache = new StatusCache();
        context.AddBridge(bridge => Register(bridge, context.Tasks, cache));
        var watcher = new TurnWatcher(context.Sessions, taskId =>
        {
            cache.Clear();
            context.Broadcast("git.changed", new GitChangedEvent(taskId), GitJson.Default.GitChangedEvent);
        });
        return watcher;
    }

    private static void Register(BridgeDispatcher bridge, TaskStore tasks, StatusCache cache)
    {
        var j = GitJson.Default;
        bridge.Handle("git.status", j.GitStatusRequest, j.IReadOnlyListGitFolderStatus, async r =>
        {
            if (!TaskStore.IsTaskId(r.TaskId)) throw new BridgeRejectException("invalid task id");
            var doc = tasks.Get(r.TaskId) ?? throw new BridgeRejectException("no such task");
            // Only folders the task names: the renderer never chooses where git runs.
            var folders = doc.Repos.Concat(doc.Worktrees.Select(w => w.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var statuses = await Task.WhenAll(folders.Select(f => Task.Run(() => cache.Get(f)))).ConfigureAwait(false);
            return (IReadOnlyList<GitFolderStatus>)statuses;
        });
    }

    internal static GitFolderStatus Read(string folder)
    {
        if (!Directory.Exists(folder)) return new GitFolderStatus(folder, true, false, null, null, 0, 0, 0);
        return GitStatusReader.Read(folder) is { } s
            ? new GitFolderStatus(folder, false, true, s.Branch, s.Upstream, s.Ahead, s.Behind, s.Changes.Count)
            : new GitFolderStatus(folder, false, false, null, null, 0, 0, 0);
    }

    /// <summary>
    /// Keeps each folder's status for a few seconds. Several windows, and Today next to a task, ask for the same
    /// folders when focus moves; one git run answers them all.
    /// </summary>
    private sealed class StatusCache
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3);
        private readonly ConcurrentDictionary<string, (GitFolderStatus Status, DateTime At)> _entries = new(StringComparer.OrdinalIgnoreCase);

        public GitFolderStatus Get(string folder)
        {
            if (_entries.TryGetValue(folder, out var hit) && DateTime.UtcNow - hit.At < MaxAge) return hit.Status;
            var status = Read(folder);
            _entries[folder] = (status, DateTime.UtcNow);
            return status;
        }

        public void Clear() => _entries.Clear();
    }

    /// <summary>Calls back when a session on a task goes from working to not working: its turn ended.</summary>
    private sealed class TurnWatcher : IDisposable
    {
        private readonly SessionManager _sessions;
        private readonly Action<string> _turnEnded;
        private readonly ConcurrentDictionary<string, bool> _busy = new(StringComparer.Ordinal);

        public TurnWatcher(SessionManager sessions, Action<string> turnEnded)
        {
            _sessions = sessions;
            _turnEnded = turnEnded;
            _sessions.Changed += OnChanged;
        }

        private void OnChanged(LiveSession s, SessionChanges changes)
        {
            bool busy;
            string taskId;
            lock (s.Gate)
            {
                busy = s.Observed ? s.ObservedStatus == "busy" : !s.Dormant && s.Conversation.TurnActive;
                taskId = s.TaskId;
            }
            var was = _busy.TryGetValue(s.Id, out var w) && w;
            _busy[s.Id] = busy;
            if (was && !busy && taskId.Length > 0) _turnEnded(taskId);
        }

        public void Dispose() => _sessions.Changed -= OnChanged;
    }
}
