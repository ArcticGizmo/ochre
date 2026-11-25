namespace Ochre.Host.Bridge;

public sealed record RequestContract(string Type, Type Request, Type Response);

public sealed record EventContract(string Name, Type Payload);

/// <summary>
/// Every message the UI may send and every event the host may push (ADR 0009). tools/Ochre.BridgeGen generates
/// ui/src/generated/bridge-types.ts from this list; the host checks its registered handlers against it at startup.
/// </summary>
public static class BridgeContract
{
    public static IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("app.info", typeof(Empty), typeof(AppInfo)),
        new("app.ready", typeof(Empty), typeof(Ack)),
        new("tasks.list", typeof(Empty), typeof(IReadOnlyList<TaskListItem>)),
        new("tasks.get", typeof(TaskIdRequest), typeof(TaskDetail)),
        new("tasks.create", typeof(CreateTaskRequest), typeof(TaskDetail)),
        new("tasks.update", typeof(UpdateTaskRequest), typeof(TaskDetail)),
        new("tasks.setState", typeof(SetStateRequest), typeof(TaskDetail)),
        new("tasks.delete", typeof(TaskIdRequest), typeof(Ack)),
        new("plan.check", typeof(CheckItemRequest), typeof(TaskDetail)),
        new("plan.add", typeof(AddItemRequest), typeof(TaskDetail)),
        new("sessions.list", typeof(Empty), typeof(IReadOnlyList<SessionSummary>)),
        new("sessions.get", typeof(SessionIdRequest), typeof(SessionSnapshot)),
        new("sessions.start", typeof(StartSessionRequest), typeof(StartSessionResult)),
        new("sessions.send", typeof(SendPromptRequest), typeof(Ack)),
        new("sessions.interrupt", typeof(SessionIdRequest), typeof(Ack)),
        new("sessions.stop", typeof(SessionIdRequest), typeof(Ack)),
        new("sessions.respond", typeof(RespondRequest), typeof(Ack)),
    ];

    public static IReadOnlyList<EventContract> Events { get; } =
    [
        new("window.shown", typeof(Empty)),
        new("tasks.changed", typeof(TaskChangedEvent)),
        new("sessions.update", typeof(SessionUpdateEvent)),
    ];
}
