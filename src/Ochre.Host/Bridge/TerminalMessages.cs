namespace Ochre.Host.Bridge;

// Bridge shapes for terminal tabs (ADR 0011). Output is broadcast to every window with a sequence number; a window
// joining a terminal takes its scrollback with terminals.attach and applies only chunks numbered after it.

public sealed record ShellProfileDto(string Id, string Name);

/// <param name="Folder">Where it opened, or the last folder the shell reported.</param>
/// <param name="Title">The title the program set; display only.</param>
/// <param name="ExitCode">Set once the shell has exited; the tab stays until closed.</param>
public sealed record TerminalDto(
    string Id,
    string Profile,
    string ProfileName,
    string Folder,
    string? TaskId,
    string? Title,
    string? StartupCommand,
    bool Running,
    int? ExitCode,
    string OpenedAt);

/// <param name="Profile">A shell profile id from terminals.profiles.</param>
/// <param name="TaskId">The task this terminal belongs to, if any.</param>
/// <param name="StartupCommand">Typed into the shell once it starts, e.g. <c>pnpm dev</c>.</param>
public sealed record OpenTerminalRequest(string Profile, string Folder, string? TaskId, string? StartupCommand, int Cols, int Rows);

public sealed record TerminalIdRequest(string Id);

public sealed record TerminalInputRequest(string Id, string Data);

public sealed record TerminalResizeRequest(string Id, int Cols, int Rows);

/// <param name="TaskId">Null detaches it from its task.</param>
public sealed record TerminalSetTaskRequest(string Id, string? TaskId);

/// <param name="Data">The scrollback so far.</param>
/// <param name="Seq">The number of the last chunk it includes.</param>
public sealed record TerminalSnapshot(string Id, string Data, long Seq);

public sealed record TerminalOutputEvent(string Id, string Data, long Seq);
