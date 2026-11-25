using System.Text.Json.Serialization;

namespace Ochre.Host.Bridge;

// Bridge shapes for tasks. Deliberately separate from Ochre.Core.Tasks so the UI contract changes only on purpose.

[JsonConverter(typeof(JsonStringEnumConverter<TaskStateDto>))]
public enum TaskStateDto
{
    [JsonStringEnumMemberName("backlog")]
    Backlog,

    [JsonStringEnumMemberName("active")]
    Active,

    [JsonStringEnumMemberName("parked")]
    Parked,

    [JsonStringEnumMemberName("done")]
    Done,
}

public sealed record TaskListItem(
    string Id,
    string Title,
    TaskStateDto State,
    string? Updated,
    int PlanDone,
    int PlanTotal,
    string? NextStep,
    string? ParkNote);

public sealed record PlanItemDto(string Id, string Text, bool Done, IReadOnlyList<PlanItemDto> Children);

public sealed record LinkDto(string Id, string Kind, string Ref, string? Title, string? Summary);

public sealed record SessionRefDto(string Id, string Cwd, string Binding, string? Role);

public sealed record ParkDto(string At, string Note);

public sealed record TaskDetail(
    string Id,
    string Title,
    TaskStateDto State,
    string? Created,
    string? Updated,
    IReadOnlyList<string> Repos,
    IReadOnlyList<LinkDto> Links,
    IReadOnlyList<SessionRefDto> Sessions,
    ParkDto? Park,
    IReadOnlyList<PlanItemDto> Plan,
    string PlanText,
    string Notes,
    IReadOnlyList<string> Log);

public sealed record TaskIdRequest(string Id);

/// <param name="Notes">The description; becomes the task's Notes section.</param>
public sealed record CreateTaskRequest(string Title, string? Notes, IReadOnlyList<string>? Repos);

/// <summary>Only the fields that are present change.</summary>
public sealed record UpdateTaskRequest(string Id, string? Title, string? Notes, IReadOnlyList<string>? Repos);

public sealed record SetStateRequest(string Id, TaskStateDto State);

public sealed record CheckItemRequest(string Id, string ItemId, bool Done);

public sealed record AddItemRequest(string Id, string Text, string? ParentId);

public sealed record TaskChangedEvent(string Id);
