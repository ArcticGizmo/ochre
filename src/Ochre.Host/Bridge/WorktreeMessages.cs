namespace Ochre.Host.Bridge;

// Bridge shapes for a task's worktrees (ADR 0012). The kernel keeps the list on the task; the module that made a
// worktree removes it.

/// <param name="Path">The checkout's folder; also its key on the task.</param>
/// <param name="Repo">The task repo it was made from.</param>
/// <param name="Provider">The provider that made it, e.g. <c>git-worktree</c>.</param>
/// <param name="Exists">The folder is still there.</param>
/// <param name="Key">The provider's own name for it, e.g. a sprig workspace key.</param>
public sealed record WorktreeDto(string Path, string Repo, string Provider, string? Branch, string? Created, bool Exists, string? Key);

public sealed record WorktreeProviderDto(string Id, string Name);

/// <param name="Repo">One of the task's repos.</param>
/// <param name="Provider">A running provider's id, from worktrees.providers.</param>
/// <param name="Branch">An existing branch to check out, or a new one to make.</param>
public sealed record WorktreeRequest(string TaskId, string Repo, string Provider, string Branch);

/// <param name="Path">Where it will go; null when the provider picks the folder as it makes it (a sprig slot).</param>
/// <param name="NewBranch">The branch will be made from <paramref name="Base"/>; otherwise the existing one is checked out.</param>
public sealed record WorktreePlanDto(string? Path, string Branch, bool NewBranch, string? Base);

public sealed record WorktreePathRequest(string TaskId, string Path);

/// <param name="Branch">What it has checked out now; null when detached or gone.</param>
/// <param name="Changed">Uncommitted and untracked paths, lost if it's removed.</param>
/// <param name="UnpushedCommits">Commits only this branch has; lost if the branch is deleted. Null when unknown.</param>
/// <param name="RunningAgents">Agents working in the folder; it can't be removed while any are.</param>
/// <param name="ProviderRunning">The module that made it is on, so it can be removed from here.</param>
/// <param name="CanDiscardChanges">Its provider removes it with changes in it when told to; otherwise they must be committed or stashed first.</param>
/// <param name="RemoveLabel">The remove button's words, from its provider ("Give slot back").</param>
/// <param name="RemoveDescription">What removing does to the folder, from its provider.</param>
public sealed record WorktreeCheckDto(bool Exists, string? Branch, int Changed, int? UnpushedCommits, int RunningAgents, bool ProviderRunning, bool CanDiscardChanges, string RemoveLabel, string RemoveDescription);

/// <param name="DiscardChanges">Remove it even with uncommitted changes (the user saw the count).</param>
/// <param name="DeleteBranch">Also delete its branch.</param>
public sealed record RemoveWorktreeRequest(string TaskId, string Path, bool DiscardChanges, bool DeleteBranch);
