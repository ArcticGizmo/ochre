using System.Text.Json.Serialization;

namespace Ochre.Host.Bridge;

// Bridge shapes for controlled sessions. The thread is sent as a list of items addressed by index; an update carries
// only the items that changed (or a reset flag, after which the UI fetches the whole thread again).

[JsonConverter(typeof(JsonStringEnumConverter<SessionStatusDto>))]
public enum SessionStatusDto
{
    [JsonStringEnumMemberName("idle")]
    Idle,

    [JsonStringEnumMemberName("busy")]
    Busy,

    [JsonStringEnumMemberName("waiting")]
    Waiting,

    /// <summary>No process (it ended, or ran before the host restarted). Sending resumes it.</summary>
    [JsonStringEnumMemberName("dormant")]
    Dormant,
}

[JsonConverter(typeof(JsonStringEnumConverter<ItemKindDto>))]
public enum ItemKindDto
{
    [JsonStringEnumMemberName("user")]
    User,

    [JsonStringEnumMemberName("assistant")]
    Assistant,

    [JsonStringEnumMemberName("note")]
    Note,

    [JsonStringEnumMemberName("permission")]
    Permission,
}

[JsonConverter(typeof(JsonStringEnumConverter<PartKindDto>))]
public enum PartKindDto
{
    [JsonStringEnumMemberName("text")]
    Text,

    [JsonStringEnumMemberName("thinking")]
    Thinking,

    [JsonStringEnumMemberName("tool")]
    Tool,
}

[JsonConverter(typeof(JsonStringEnumConverter<ToolStatusDto>))]
public enum ToolStatusDto
{
    [JsonStringEnumMemberName("running")]
    Running,

    [JsonStringEnumMemberName("done")]
    Done,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<ResolutionDto>))]
public enum ResolutionDto
{
    [JsonStringEnumMemberName("pending")]
    Pending,

    [JsonStringEnumMemberName("allowed")]
    Allowed,

    [JsonStringEnumMemberName("denied")]
    Denied,

    [JsonStringEnumMemberName("expired")]
    Expired,

    [JsonStringEnumMemberName("answeredElsewhere")]
    AnsweredElsewhere,
}

/// <param name="Description">What it was asked to do.</param>
/// <param name="Progress">Its latest progress line, for a background agent that reports one.</param>
public sealed record SubagentDto(string Description, string? Progress);

/// <param name="Id">The id the session was launched with (also its id on the task).</param>
/// <param name="Activity">What it's doing in the current turn (the running tool, "thinking", "writing"); null when idle.</param>
public sealed record SessionSummary(
    string Id,
    string TaskId,
    string Cwd,
    string? Role,
    SessionStatusDto Status,
    string StartedAt,
    string? Activity,
    IReadOnlyList<SubagentDto> Subagents);

/// <param name="Model">The model the user picked, else the one the CLI reported; empty before either.</param>
/// <param name="Effort">The effort level the user picked, or "auto".</param>
public sealed record SessionHeader(
    string Id,
    string TaskId,
    string Cwd,
    string? Role,
    SessionStatusDto Status,
    string Model,
    string Effort,
    string PermissionMode,
    double CostUsd,
    long ContextTokens,
    int QueuedPrompts,
    string? Activity,
    IReadOnlyList<SubagentDto> Subagents);

/// <param name="Diff">For an edit or write: the change as diff lines ("+", "-" or " " followed by the text).</param>
public sealed record AssistantPartDto(
    PartKindDto Kind,
    string Text,
    bool Streaming,
    string? ToolName,
    string? InputJson,
    ToolStatusDto? Status,
    string? Result,
    IReadOnlyList<string>? Diff);

public sealed record QuestionOptionDto(string Label, string Description);

public sealed record QuestionDto(string Question, string Header, IReadOnlyList<QuestionOptionDto> Options, bool MultiSelect);

/// <param name="Plan">For a plan approval (ExitPlanMode): the plan markdown.</param>
public sealed record PermissionDto(
    string RequestId,
    string ToolName,
    string Description,
    string InputJson,
    string? SuggestedMode,
    ResolutionDto Resolution,
    IReadOnlyList<QuestionDto>? Questions,
    string? Plan,
    string? AnswerSummary,
    string? SwitchedMode);

/// <param name="Text">User text, or a note's text.</param>
/// <param name="Error">A note reporting a failure.</param>
/// <param name="Complete">An assistant item whose turn has finished.</param>
public sealed record SessionItemDto(
    int Index,
    ItemKindDto Kind,
    string Text,
    bool Error,
    bool Complete,
    IReadOnlyList<AssistantPartDto>? Parts,
    PermissionDto? Permission);

public sealed record SessionSnapshot(SessionHeader Header, bool TurnActive, IReadOnlyList<SessionItemDto> Items);

public sealed record SessionIdRequest(string Id);

/// <param name="FirstPrompt">Sent as soon as the session starts; empty leaves it waiting for a message.</param>
/// <param name="TrustFolder">The user agreed to trust <c>Cwd</c> (asked after a <c>NeedsTrust</c> reply).</param>
public sealed record StartSessionRequest(string TaskId, string Cwd, string? Role, string? PermissionMode, string? FirstPrompt, bool TrustFolder);

/// <param name="NeedsTrust">Nothing started: Claude Code hasn't been told to trust the folder. Ask, then retry with TrustFolder.</param>
public sealed record StartSessionResult(string? SessionId, bool NeedsTrust);

public sealed record SendPromptRequest(string Id, string Text);

/// <param name="Model">A Claude model id such as <c>claude-opus-5-5</c>, or an alias the CLI accepts.</param>
public sealed record SetModelRequest(string Id, string Model);

/// <param name="Effort">"auto" or one of the CLI's levels (low, medium, high, xhigh, max).</param>
public sealed record SetEffortRequest(string Id, string Effort);

public sealed record QuestionAnswerDto(string Question, IReadOnlyList<string> Labels);

/// <param name="SwitchMode">With an allow: the permission mode to switch to afterwards (the CLI's suggestion).</param>
/// <param name="Answers">For a question: the chosen option labels per question.</param>
public sealed record RespondRequest(string Id, string RequestId, bool Allow, string? SwitchMode, IReadOnlyList<QuestionAnswerDto>? Answers);

/// <param name="Reset">The thread changed shape; fetch it again with sessions.get.</param>
/// <param name="Items">The items that changed, by index. An index past the end is a new item.</param>
public sealed record SessionUpdateEvent(string Id, bool Reset, SessionHeader Header, bool TurnActive, IReadOnlyList<SessionItemDto> Items);
