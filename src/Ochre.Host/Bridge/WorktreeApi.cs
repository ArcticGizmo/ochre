using System.Globalization;
using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Host.Bridge;

/// <summary>
/// A task's worktrees (ADR 0012). The renderer names only a task, one of its repos, a provider and a branch; the
/// folder comes from the provider, and removal acts only on a worktree the task records.
/// </summary>
internal static class WorktreeApi
{
    private const string UserAuthor = "user";
    private const int MaxBranch = 100;

    public static void Register(BridgeDispatcher bridge, TaskStore store, ModuleHost modules, SessionManager sessions)
    {
        var j = AppJson.Default;

        bridge.Handle("worktrees.providers", j.Empty, j.IReadOnlyListWorktreeProviderDto, _ =>
            (IReadOnlyList<WorktreeProviderDto>)modules.WorktreeProviders.Select(p => new WorktreeProviderDto(p.Id, p.Name)).ToList());

        bridge.Handle("worktrees.plan", j.WorktreeRequest, j.WorktreePlanDto, async r =>
        {
            var (_, repo, provider, branch) = Check(r, store, modules);
            var plan = await Refusing(() => provider.PlanAsync(repo, branch, CancellationToken.None)).ConfigureAwait(true);
            return new WorktreePlanDto(plan.Path, plan.Branch, plan.NewBranch, plan.Base);
        });

        bridge.Handle("worktrees.create", j.WorktreeRequest, j.WorktreeDto, async r =>
        {
            var (id, repo, provider, branch) = Check(r, store, modules);
            var plan = await Refusing(() => provider.CreateAsync(repo, branch, CancellationToken.None)).ConfigureAwait(true);
            var path = plan.Path ?? throw new BridgeRejectException($"{provider.Name} made the checkout but didn't say where");
            var worktree = new TaskWorktree(path, repo, provider.Id, plan.Branch, DateTimeOffset.UtcNow, plan.Key);
            try
            {
                await store.UpdateAsync(id, UserAuthor, doc =>
                {
                    doc.AddWorktree(worktree);
                    return $"added worktree {plan.Branch} ({provider.Id})";
                }).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or TaskFormatException)
            {
                throw new BridgeRejectException($"the worktree was made at {path}, but the task couldn't record it: {ex.Message}");
            }
            return ToDto(worktree);
        });

        bridge.Handle("worktrees.check", j.WorktreePathRequest, j.WorktreeCheckDto, async r =>
        {
            var worktree = Find(r.TaskId, r.Path, store);
            var agents = RunningAgentsIn(worktree.Path, sessions);
            if (modules.WorktreeProvider(worktree.Provider) is not { } provider)
                return new WorktreeCheckDto(Directory.Exists(worktree.Path), worktree.Branch, 0, null, agents, false, false, "Remove", "");
            var check = await Refusing(() => provider.CheckAsync(worktree, CancellationToken.None)).ConfigureAwait(true);
            return new WorktreeCheckDto(check.Exists, check.Branch, check.Changed, check.UnpushedCommits, agents, true, check.CanDiscardChanges, provider.RemoveLabel, provider.RemoveDescription);
        });

        bridge.Handle("worktrees.remove", j.RemoveWorktreeRequest, j.Ack, async r =>
        {
            var worktree = Find(r.TaskId, r.Path, store);
            var provider = modules.WorktreeProvider(worktree.Provider)
                ?? throw new BridgeRejectException($"nothing that can remove a {worktree.Provider} checkout is running; turn its module on");
            if (RunningAgentsIn(worktree.Path, sessions) > 0) throw new BridgeRejectException("agents are working in this folder; stop them first");
            await Refusing(async () =>
            {
                await provider.RemoveAsync(worktree, r.DiscardChanges, r.DeleteBranch, CancellationToken.None).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(true);
            try
            {
                await store.UpdateAsync(r.TaskId, UserAuthor, doc =>
                    doc.RemoveWorktree(worktree.Path) ? $"removed worktree {worktree.Branch ?? worktree.Path}{(r.DeleteBranch ? " and its branch" : "")}" : null).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or TaskFormatException)
            {
                throw new BridgeRejectException($"the worktree was removed, but the task couldn't be updated: {ex.Message}");
            }
            return new Ack();
        });
    }

    public static WorktreeDto ToDto(TaskWorktree w) =>
        new(w.Path, w.Repo, w.Provider, w.Branch, w.Created?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), Directory.Exists(w.Path), w.Key);

    private static (string Id, string Repo, IWorktreeProvider Provider, string Branch) Check(WorktreeRequest r, TaskStore store, ModuleHost modules)
    {
        if (!TaskStore.IsTaskId(r.TaskId)) throw new BridgeRejectException("invalid task id");
        var doc = store.Get(r.TaskId) ?? throw new BridgeRejectException("no such task");
        var repo = doc.Repos.FirstOrDefault(x => string.Equals(x, r.Repo, StringComparison.OrdinalIgnoreCase))
            ?? throw new BridgeRejectException("choose one of the task's repos");
        var provider = modules.WorktreeProvider(r.Provider) ?? throw new BridgeRejectException("that kind of worktree isn't available");
        var branch = r.Branch.Trim();
        if (branch.Length == 0 || branch.Length > MaxBranch) throw new BridgeRejectException($"a branch name is 1 to {MaxBranch} characters");
        return (r.TaskId, repo, provider, branch);
    }

    private static TaskWorktree Find(string taskId, string path, TaskStore store)
    {
        if (!TaskStore.IsTaskId(taskId)) throw new BridgeRejectException("invalid task id");
        var doc = store.Get(taskId) ?? throw new BridgeRejectException("no such task");
        return doc.Worktrees.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new BridgeRejectException("the task has no such worktree");
    }

    private static int RunningAgentsIn(string folder, SessionManager sessions) =>
        sessions.All.Count(s =>
        {
            bool running;
            lock (s.Gate) running = s.Observed ? s.ObservedStatus != "closed" : !s.Dormant;
            return running && BindSuggestions.IsInside(s.Cwd, folder);
        });

    private static async Task<T> Refusing<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (WorktreeException ex)
        {
            throw new BridgeRejectException(ex.Message);
        }
    }
}
