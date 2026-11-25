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
        new("settings.get", typeof(Empty), typeof(SettingsDto)),
        new("settings.update", typeof(SettingsDto), typeof(SettingsDto)),
        new("tasks.list", typeof(Empty), typeof(IReadOnlyList<TaskListItem>)),
        new("tasks.get", typeof(TaskIdRequest), typeof(TaskDetail)),
        new("tasks.create", typeof(CreateTaskRequest), typeof(TaskDetail)),
        new("tasks.update", typeof(UpdateTaskRequest), typeof(TaskDetail)),
        new("tasks.setState", typeof(SetStateRequest), typeof(TaskDetail)),
        new("tasks.delete", typeof(TaskIdRequest), typeof(Ack)),
        new("tasks.reviewAgentChanges", typeof(TaskIdRequest), typeof(TaskDetail)),
        new("tasks.park", typeof(ParkTaskRequest), typeof(Ack)),
        new("plan.check", typeof(CheckItemRequest), typeof(TaskDetail)),
        new("plan.add", typeof(AddItemRequest), typeof(TaskDetail)),
        new("sessions.list", typeof(Empty), typeof(IReadOnlyList<SessionSummary>)),
        new("sessions.get", typeof(SessionIdRequest), typeof(SessionSnapshot)),
        new("sessions.start", typeof(StartSessionRequest), typeof(StartSessionResult)),
        new("sessions.send", typeof(SendPromptRequest), typeof(Ack)),
        new("sessions.interrupt", typeof(SessionIdRequest), typeof(Ack)),
        new("sessions.stop", typeof(SessionIdRequest), typeof(Ack)),
        new("sessions.respond", typeof(RespondRequest), typeof(Ack)),
        new("sessions.setModel", typeof(SetModelRequest), typeof(Ack)),
        new("sessions.setEffort", typeof(SetEffortRequest), typeof(Ack)),
        new("sessions.bind", typeof(BindSessionRequest), typeof(Ack)),
        new("sessions.unbind", typeof(SessionIdRequest), typeof(Ack)),
    ];

    public static IReadOnlyList<EventContract> Events { get; } =
    [
        new("window.shown", typeof(Empty)),
        new("tasks.changed", typeof(TaskChangedEvent)),
        new("sessions.update", typeof(SessionUpdateEvent)),
        // A session appeared or went, or an observed one's suggestions may have changed: fetch sessions.list again.
        new("sessions.changed", typeof(Empty)),
    ];
}
