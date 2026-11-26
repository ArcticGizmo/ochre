namespace Ochre.Core.Tasks;

public enum TaskState
{
    Backlog,
    Active,
    Parked,
    Done,
}

public sealed record TaskLink(string Id, string Kind, string Ref, string? Title, string? Summary);

/// <param name="Binding">"launched" (started from the task) or "confirmed" (an observed session the user bound).</param>
public sealed record TaskSessionRef(string Id, string Cwd, string? ConfigDir, string Binding, string? Role);

public sealed record ParkNote(DateTimeOffset At, string Note);

/// <summary>A checkout made for the task by a worktree provider (ADR 0012).</summary>
/// <param name="Path">The checkout's folder; also its key on the task.</param>
/// <param name="Repo">The task repo it was made from.</param>
/// <param name="Provider">The provider that made it and can remove it (<c>git-worktree</c>).</param>
/// <param name="Branch">The branch checked out there, as created.</param>
/// <param name="Key">The provider's own name for it, e.g. a sprig workspace key (<c>shop/2</c>).</param>
public sealed record TaskWorktree(string Path, string Repo, string Provider, string? Branch, DateTimeOffset? Created, string? Key = null);

/// <param name="Line">Index of the item's line in the task body; only valid for the document it came from.</param>
public sealed record PlanItem(string? Id, string Text, bool Done, int Indent, int Line, IReadOnlyList<PlanItem> Children)
{
    public IEnumerable<PlanItem> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var item in child.SelfAndDescendants())
            yield return item;
    }
}

public sealed record LogEntry(string Text);

/// <summary>The list view of a task: enough to sort, filter and show "where was I".</summary>
public sealed record TaskSummary(
    string Id,
    string Title,
    TaskState State,
    DateTimeOffset? Updated,
    int? Order,
    int PlanDone,
    int PlanTotal,
    string? NextStep,
    string? ParkNote,
    DateTimeOffset? ParkedAt,
    IReadOnlyList<TaskLink> Links,
    IReadOnlyList<TaskSessionRef> Sessions,
    IReadOnlyList<string> Repos,
    IReadOnlyList<TaskWorktree> Worktrees);

public sealed class TaskFormatException(string message) : Exception(message);
