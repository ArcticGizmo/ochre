using Ochre.Core.Tasks;

namespace Ochre.Core.Modules;

/// <summary>Where a new checkout would go, worked out before anything is made so the user can see it first.</summary>
/// <param name="NewBranch">The branch doesn't exist yet and will be made from <paramref name="Base"/>.</param>
/// <param name="Base">What a new branch starts from (the repo's current branch), for display.</param>
internal sealed record WorktreePlan(string Path, string Branch, bool NewBranch, string? Base);

/// <summary>What removing a checkout would lose, for the confirmation dialog.</summary>
/// <param name="Exists">The folder is still there; when it isn't, removing only tidies up.</param>
/// <param name="Changed">Uncommitted and untracked paths in the checkout.</param>
/// <param name="Branch">The branch it has checked out; null when detached or the branch is gone.</param>
/// <param name="UnpushedCommits">Commits on the branch that no other branch or remote has, so deleting the branch
/// loses them; null when that couldn't be worked out.</param>
internal sealed record WorktreeCheck(bool Exists, string? Branch, int Changed, int? UnpushedCommits);

/// <summary>A refusal worth showing the user as it is ("not a git repository", git's own error).</summary>
internal sealed class WorktreeException(string message) : Exception(message);

/// <summary>
/// Makes and removes a checkout of a task repo on its own branch (ADR 0012). The kernel records what it made on
/// the task (the task file's <c>worktrees</c> list, the one list of a task's checkouts) and asks the provider that
/// made a checkout to remove it. Failures the user should see are <see cref="WorktreeException"/>s.
/// </summary>
internal interface IWorktreeProvider
{
    /// <summary>Stable; recorded on the task (<c>git-worktree</c>).</summary>
    string Id { get; }

    /// <summary>Shown in dialogs ("Git worktree").</summary>
    string Name { get; }

    Task<WorktreePlan> PlanAsync(string repo, string branch, CancellationToken ct);

    /// <summary>Makes the checkout <see cref="PlanAsync"/> describes, worked out afresh.</summary>
    Task<WorktreePlan> CreateAsync(string repo, string branch, CancellationToken ct);

    Task<WorktreeCheck> CheckAsync(TaskWorktree worktree, CancellationToken ct);

    /// <summary>Removes the checkout's folder (refusing if it has changes, unless <paramref name="discardChanges"/>),
    /// then its branch when asked.</summary>
    Task RemoveAsync(TaskWorktree worktree, bool discardChanges, bool deleteBranch, CancellationToken ct);
}
