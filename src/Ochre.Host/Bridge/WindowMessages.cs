using System.Text.Json.Serialization;

namespace Ochre.Host.Bridge;

// Bridge shapes for windows: which task a window shows, and opening a task in a window of its own.

/// <param name="Task">The task this window opened on (reopened from the saved layout, or opened for a task), else null.</param>
/// <param name="Jump">The jump hotkey opened it: show the "jump to task" picker.</param>
public sealed record WindowInfo(string? Task, bool Jump);

/// <param name="Task">The task the page shows now, or null when it shows none (Today, Board, a module view).</param>
public sealed record WindowTaskRequest(string? Task);

/// <param name="Count">How many files this window holds unsaved edits to; closing it asks first while this isn't 0.</param>
public sealed record WindowUnsavedRequest(int Count);

/// <param name="Quit">The question came from quitting Ochre: carry on quitting rather than close only this window.</param>
public sealed record WindowCloseRequest(bool Quit);

/// <summary>Sent to one window: the user asked to close it (or to quit) while it holds unsaved edits. Ask them.</summary>
public sealed record WindowCloseRequestedEvent(bool Quit);

[JsonConverter(typeof(JsonStringEnumConverter<OpenTaskChoiceDto>))]
public enum OpenTaskChoiceDto
{
    /// <summary>Answer <c>elsewhere</c> when only a window on another desktop shows it, so the user can choose.</summary>
    [JsonStringEnumMemberName("ask")]
    Ask,

    [JsonStringEnumMemberName("bring")]
    Bring,

    [JsonStringEnumMemberName("new")]
    New,
}

[JsonConverter(typeof(JsonStringEnumConverter<OpenTaskOutcomeDto>))]
public enum OpenTaskOutcomeDto
{
    [JsonStringEnumMemberName("opened")]
    Opened,

    [JsonStringEnumMemberName("focused")]
    Focused,

    [JsonStringEnumMemberName("moved")]
    Moved,

    [JsonStringEnumMemberName("elsewhere")]
    Elsewhere,
}

public sealed record OpenTaskRequest(string Id, OpenTaskChoiceDto Choice);

public sealed record OpenTaskResult(OpenTaskOutcomeDto Outcome);
