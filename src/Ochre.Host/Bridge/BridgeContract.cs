using Ochre.Core.Bridge;

namespace Ochre.Host.Bridge;

/// <summary>
/// The kernel's messages and events (ADR 0009); each module declares its own (ADR 0012). tools/Ochre.BridgeGen
/// generates ui/src/generated/bridge-types.ts from these plus every first-party module's, and the host checks its
/// registered handlers against the kernel's and the started modules' lists.
/// </summary>
public static class BridgeContract
{
    public static IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("app.info", typeof(Empty), typeof(AppInfo)),
        new("app.ready", typeof(Empty), typeof(Ack)),
        new("app.openUrl", typeof(OpenUrlRequest), typeof(Ack)),
        new("app.restart", typeof(Empty), typeof(Ack)),
        new("app.update", typeof(Empty), typeof(UpdateDto)),
        new("app.checkForUpdate", typeof(Empty), typeof(UpdateDto)),
        new("app.applyUpdate", typeof(Empty), typeof(UpdateDto)),
        new("settings.get", typeof(Empty), typeof(SettingsDto)),
        new("settings.update", typeof(SettingsUpdateRequest), typeof(SettingsDto)),
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
        new("attention.list", typeof(Empty), typeof(AttentionSnapshot)),
        new("attention.seen", typeof(AttentionSeenRequest), typeof(Ack)),
        new("attention.dismiss", typeof(AttentionItemRequest), typeof(Ack)),
        new("attention.viewing", typeof(AttentionViewingRequest), typeof(Ack)),
        new("worktrees.providers", typeof(Empty), typeof(IReadOnlyList<WorktreeProviderDto>)),
        new("worktrees.plan", typeof(WorktreeRequest), typeof(WorktreePlanDto)),
        new("worktrees.create", typeof(WorktreeRequest), typeof(WorktreeDto)),
        new("worktrees.check", typeof(WorktreePathRequest), typeof(WorktreeCheckDto)),
        new("worktrees.remove", typeof(RemoveWorktreeRequest), typeof(Ack)),
        new("window.get", typeof(Empty), typeof(WindowInfo)),
        new("window.setTask", typeof(WindowTaskRequest), typeof(Ack)),
        new("windows.openTask", typeof(OpenTaskRequest), typeof(OpenTaskResult)),
    ];

    public static IReadOnlyList<EventContract> Events { get; } =
    [
        new("window.shown", typeof(Empty)),
        // Sent to one window: the jump hotkey brought it forward, so show the "jump to task" picker.
        new("window.jump", typeof(Empty)),
        new("tasks.changed", typeof(TaskChangedEvent)),
        new("sessions.update", typeof(SessionUpdateEvent)),
        // A session appeared or went, or an observed one's suggestions may have changed: fetch sessions.list again.
        new("sessions.changed", typeof(Empty)),
        // What needs the user changed; carries the whole list, which is short.
        new("attention.changed", typeof(AttentionSnapshot)),
        // Checking for, finding or downloading an update.
        new("app.updateChanged", typeof(UpdateDto)),
    ];
}
