using Ochre.Core.Git;
using Ochre.Core.Modules;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Sprig;

/// <summary>
/// A sprig slot for a task: <c>sprig checkout -b &lt;branch&gt;</c> checks the branch out in a slot of worktrees with
/// its own ports and files, starting nothing. Giving it back is <c>sprig free</c>, which keeps the slot (and its
/// data) for the next branch. Sprig chooses the slot and its folder, so a plan names the folder only when the branch
/// is in a slot already.
/// </summary>
internal sealed class SprigWorktreeProvider(SprigWatch watch) : IWorktreeProvider
{
    public string Id => "sprig";
    public string Name => "Sprig slot";
    public string RemoveLabel => "Give slot back";
    public string RemoveDescription => "Sprig stops the slot's apps and infra and detaches its worktree, keeping the slot's data, ports and values for its next branch. The folder stays as sprig's free slot, and the slot comes off the task.";

    public Task<WorktreePlan> PlanAsync(string repo, string branch, CancellationToken ct) => Task.Run(() => Plan(repo, branch), ct);

    public Task<WorktreePlan> CreateAsync(string repo, string branch, CancellationToken ct) => Task.Run(() =>
    {
        var plan = Plan(repo, branch);
        var (result, checkout) = SprigCli.Checkout(repo, plan.Branch);
        if (result.Ok && checkout is not null)
            return new WorktreePlan(checkout.Folder, checkout.Branch ?? plan.Branch, checkout.BranchCreated, plan.Base, checkout.Key);

        // Sprig can make the slot and then refuse it (the new branch has no sprig.yaml, say). The slot and branch are
        // real, so the task records them, with sprig's error showing beside the slot, rather than leaving a slot that
        // nothing tracks or gives back.
        if (result.Ok || MadeSlot(repo, plan.Branch, ct) is not { } made) throw new WorktreeException(Explain(result));
        return new WorktreePlan(made.Folder, plan.Branch, plan.NewBranch, plan.Base, made.Key);
    }, ct);

    // The slot sprig holds for this branch and repo, from the watch (which may trail the checkout by a moment). Only a
    // slot folder beside the repo counts, never the repo's own checkout.
    private (string Folder, string Key)? MadeSlot(string repo, string branch, CancellationToken ct)
    {
        var slotPrefix = SprigFolders.Normalize(repo) + "--";
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool InSlot(string dir) => SprigFolders.Normalize(dir) is { } d && d.StartsWith(slotPrefix, cmp);
        for (var waited = TimeSpan.Zero; ; waited += TimeSpan.FromMilliseconds(250))
        {
            var slot = watch.Current.Workspaces.FirstOrDefault(w => !w.IsFree && w.Branch == branch && w.Dirs.Any(InSlot));
            if (slot?.Dirs.FirstOrDefault(InSlot) is { } folder) return (folder, slot.Key);
            if (waited >= TimeSpan.FromSeconds(5) || ct.IsCancellationRequested) return null;
            Thread.Sleep(250);
        }
    }

    public Task<WorktreeCheck> CheckAsync(TaskWorktree worktree, CancellationToken ct) => Task.Run(() =>
    {
        var slot = Slot(worktree);
        // Already given back (in sprig's own UI, say): there's nothing left of the task's in it.
        var exists = slot is not null && Directory.Exists(worktree.Path);
        var changed = exists ? GitStatusReader.Read(worktree.Path)?.Changes.Count ?? 0 : 0;
        var branch = slot?.Branch ?? worktree.Branch;
        var root = GitCheckoutScanner.FindRoot(worktree.Repo) ?? worktree.Repo;
        int? unpushed = branch is null ? 0 : GitBranches.UnpushedCommits(root, branch);
        if (unpushed is null && GitBranches.Exists(root, branch!) == false) (branch, unpushed) = (null, 0);
        return new WorktreeCheck(exists, branch, changed, unpushed, CanDiscardChanges: false);
    }, ct);

    public Task RemoveAsync(TaskWorktree worktree, bool discardChanges, bool deleteBranch, CancellationToken ct) => Task.Run(() =>
    {
        var slot = Slot(worktree);
        var branch = slot?.Branch ?? worktree.Branch;
        if (slot is not null)
        {
            var result = SprigCli.Free(slot.Orchestration, slot.Name);
            if (!result.Ok) throw new WorktreeException(Explain(result));
        }
        var root = GitCheckoutScanner.FindRoot(worktree.Repo) ?? worktree.Repo;
        if (deleteBranch && branch is not null && GitBranches.Exists(root, branch) == true && GitBranches.Delete(root, branch) is { } why)
            throw new WorktreeException($"the slot is given back, but {branch} wasn't deleted: {why}");
    }, ct);

    private WorktreePlan Plan(string repo, string branch)
    {
        branch = branch.Trim();
        if (!GitBranches.IsValidName(branch)) throw new WorktreeException(GitBranches.NameRule);
        var root = GitCheckoutScanner.FindRoot(repo) ?? throw new WorktreeException($"{repo} isn't a git repository");
        // Sprig must know the repo before it can make a slot for it; its refusal says how to fix that.
        var info = SprigCli.Info(repo);
        if (!info.Ok) throw new WorktreeException(Explain(info));

        // A branch already in a slot is checked out there again, so that slot's folder is where it goes.
        var already = watch.Current.Workspaces.FirstOrDefault(w => !w.IsFree && w.Branch == branch && w.Dirs.Any(d => SameRepo(d, repo)));
        var folder = already?.Dirs.FirstOrDefault(d => SameRepo(d, repo));
        var exists = GitBranches.Exists(root, branch) ?? throw new WorktreeException($"git couldn't say whether {branch} exists in {root}");
        return new WorktreePlan(folder, branch, !exists, exists ? null : "the repo's default branch", already?.Key);
    }

    // The slot the task's entry names, while sprig still has it checked out for a branch.
    private SprigWorkspace? Slot(TaskWorktree worktree)
    {
        var status = watch.Current;
        // Before the first snapshot every slot would look given back, and removing would skip `sprig free`.
        if (!status.Seen)
            throw new WorktreeException(watch.Feed == SprigFeed.NotInstalled ? "sprig isn't installed" : "Ochre hasn't heard from sprig yet; try again in a moment");
        var slot = worktree.Key is { } key ? status.Find(key) : SprigFolders.WorkspaceOf(status, worktree.Path);
        return slot is { IsFree: false } ? slot : null;
    }

    // A slot's folder for this repo sits beside it as "<repo>--<n>", which is how sprig lays slots out.
    private static bool SameRepo(string dir, string repo)
    {
        var d = SprigFolders.Normalize(dir);
        var r = SprigFolders.Normalize(repo);
        if (d is null || r is null) return false;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(d, r, cmp) || d.StartsWith(r + "--", cmp);
    }

    internal static string Explain(SprigActionResult result) =>
        result.NeedsSprig ? $"{result.Message}. Open sprig to sort it out, then try again." : result.Message;
}
