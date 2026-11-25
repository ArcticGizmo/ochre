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
    string? ParkNote);

public sealed class TaskFormatException(string message) : Exception(message);
