using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Core.Tasks;
using Ochre.Modules.Sprig.Bridge;

namespace Ochre.Modules.Sprig;

/// <summary>
/// Sprig for tasks (M4 slice 3): a <c>sprig</c> worktree provider, so a task's checkout can be a slot with its own
/// ports and files, and the apps of every workspace a task's folders are in, read from one <c>sprig watch</c>.
/// Nothing starts by itself: starting, stopping and giving a slot back are the user's clicks.
/// </summary>
internal sealed class SprigModule : IModule
{
    private static readonly string[] Actions = ["start", "stop", "restart"];

    public string Id => "sprig";
    public string Name => "Sprig";
    public string Description => "Lets a task's worktree be a sprig slot with its own ports, and shows and runs the apps of the sprig workspaces a task's folders are in. Needs sprig installed.";
    public bool DefaultEnabled => false;

    public IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("sprig.status", typeof(SprigTaskRequest), typeof(SprigTaskStatus)),
        new("sprig.act", typeof(SprigActRequest), typeof(SprigActResult)),
        new("sprig.log", typeof(SprigLogRequest), typeof(SprigLogDto)),
        new("sprig.open", typeof(SprigWorkspaceRequest), typeof(SprigActResult)),
    ];

    public IReadOnlyList<EventContract> Events { get; } =
    [
        // What sprig reports changed, or the connection to it did: fetch sprig.status again.
        new("sprig.changed", typeof(Empty)),
    ];

    public IDisposable Start(IModuleContext context)
    {
        var watch = new SprigWatch(context.Log);
        watch.Changed += () => context.Broadcast("sprig.changed", new Empty(), SprigJson.Default.Empty);
        context.AddWorktreeProvider(new SprigWorktreeProvider(watch));
        context.AddBridge(bridge => Register(bridge, context.Tasks, watch));
        watch.Start();
        return watch;
    }

    private static void Register(BridgeDispatcher bridge, TaskStore tasks, SprigWatch watch)
    {
        var j = SprigJson.Default;

        bridge.Handle("sprig.status", j.SprigTaskRequest, j.SprigTaskStatus, r =>
        {
            var folders = Folders(tasks, r.TaskId);
            var status = watch.Current;
            var matched = Matched(status, folders);
            return new SprigTaskStatus(FeedWord(watch.Feed), status.HostRunning, matched.Select(m => Dto(m.Workspace, m.Folders)).ToList());
        });

        bridge.Handle("sprig.act", j.SprigActRequest, j.SprigActResult, async r =>
        {
            if (!Actions.Contains(r.Action)) throw new BridgeRejectException("unknown action");
            var workspace = Find(tasks, watch, r.TaskId, r.Key);
            if (r.App is not null && workspace.Apps.All(a => a.Name != r.App)) throw new BridgeRejectException("no such app");
            var action = r.Action switch
            {
                "start" => SprigAction.Start,
                "stop" => SprigAction.Stop,
                _ => SprigAction.Restart,
            };
            var result = await Task.Run(() => SprigCli.Run(action, workspace, r.App)).ConfigureAwait(false);
            return Result(result);
        });

        bridge.Handle("sprig.log", j.SprigLogRequest, j.SprigLogDto, async r =>
        {
            var workspace = Find(tasks, watch, r.TaskId, r.Key);
            var app = workspace.Apps.FirstOrDefault(a => a.Name == r.App) ?? throw new BridgeRejectException("no such app");
            // The path comes from sprig, never from the renderer.
            if (app.LogFile is not { Length: > 0 } path) throw new BridgeRejectException("sprig keeps no log for this app");
            var tail = await Task.Run(() => LogTail.Read(path)).ConfigureAwait(false) ?? throw new BridgeRejectException($"the log can't be read: {path}");
            return new SprigLogDto(app.Name, path, tail.Text, tail.Truncated);
        });

        bridge.Handle("sprig.open", j.SprigWorkspaceRequest, j.SprigActResult, async r =>
        {
            var workspace = Find(tasks, watch, r.TaskId, r.Key);
            return Result(await Task.Run(() => SprigCli.Open(workspace)).ConfigureAwait(false));
        });
    }

    // The task's repos and worktrees, as it records them.
    private static List<string> Folders(TaskStore tasks, string taskId)
    {
        if (!TaskStore.IsTaskId(taskId)) throw new BridgeRejectException("invalid task id");
        var doc = tasks.Get(taskId) ?? throw new BridgeRejectException("no such task");
        return doc.Repos.Concat(doc.Worktrees.Select(w => w.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The workspaces the folders are in, each with the folders inside it, in the folders' order.</summary>
    internal static List<(SprigWorkspace Workspace, List<string> Folders)> Matched(SprigStatus status, IEnumerable<string> folders)
    {
        var matched = new List<(SprigWorkspace Workspace, List<string> Folders)>();
        foreach (var folder in folders)
        {
            if (SprigFolders.WorkspaceOf(status, folder) is not { } w) continue;
            var i = matched.FindIndex(m => m.Workspace.Key == w.Key);
            if (i >= 0) matched[i].Folders.Add(folder);
            else matched.Add((w, [folder]));
        }
        return matched;
    }

    // Only a workspace one of the task's folders is in: the renderer can't act on sprig at large.
    private static SprigWorkspace Find(TaskStore tasks, SprigWatch watch, string taskId, string key) =>
        Matched(watch.Current, Folders(tasks, taskId)).Select(m => m.Workspace).FirstOrDefault(w => w.Key == key)
            ?? throw new BridgeRejectException("that sprig workspace isn't one of the task's");

    private static SprigActResult Result(SprigActionResult r) => new(r.Ok, r.Ok ? null : r.Code, r.Ok ? null : r.Message, r.NeedsSprig);

    private static string FeedWord(SprigFeed feed) => feed switch
    {
        SprigFeed.Live => "live",
        SprigFeed.Reconnecting => "reconnecting",
        SprigFeed.NotInstalled => "notInstalled",
        _ => "connecting",
    };

    private static SprigWorkspaceDto Dto(SprigWorkspace w, IReadOnlyList<string> folders)
    {
        var (ready, of) = w.ReadyOf;
        return new SprigWorkspaceDto(
            w.Key,
            w.Label,
            w.Shown.ToString().ToLowerInvariant(),
            w.StateText,
            w.InTrouble,
            w.Activity.ToString().ToLowerInvariant(),
            ready,
            of,
            w.Error,
            folders,
            w.Apps.Select(a => new SprigAppDto(a.Name, a.ShortName, a.State, a.Port, a.Exit, a.Error, a.Doing, a.LogFile is { Length: > 0 })).ToList());
    }
}
