using System.Text.Json.Serialization;
using Ochre.Core.Bridge;

namespace Ochre.Modules.Terminals.Bridge;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Empty))]
[JsonSerializable(typeof(Ack))]
[JsonSerializable(typeof(IReadOnlyList<ShellProfileDto>))]
[JsonSerializable(typeof(IReadOnlyList<TerminalDto>))]
[JsonSerializable(typeof(TerminalDto))]
[JsonSerializable(typeof(OpenTerminalRequest))]
[JsonSerializable(typeof(TerminalIdRequest))]
[JsonSerializable(typeof(TerminalInputRequest))]
[JsonSerializable(typeof(TerminalResizeRequest))]
[JsonSerializable(typeof(TerminalSetTaskRequest))]
[JsonSerializable(typeof(TerminalKeepScrollbackRequest))]
[JsonSerializable(typeof(TerminalSnapshot))]
[JsonSerializable(typeof(TerminalOutputEvent))]
internal sealed partial class TerminalsJson : JsonSerializerContext;

// Bridge shapes for terminal tabs (ADR 0011). Output is broadcast to every window with a sequence number; a window
// joining a terminal takes its scrollback with terminals.attach and applies only chunks numbered after it.

public sealed record ShellProfileDto(string Id, string Name);

/// <param name="Folder">Where it opened, or the last folder the shell reported.</param>
/// <param name="Title">The title the program set; display only.</param>
/// <param name="ExitCode">Set once the shell has exited; the tab stays until closed.</param>
/// <param name="KeepScrollback">Its scrollback is saved, encrypted, for the next start.</param>
/// <param name="RestoredFrom">Brought back after Ochre restarted: when it was last saved. Null once dismissed.</param>
/// <param name="OfferRerun">Restored with a startup command that hasn't been run again.</param>
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
    string OpenedAt,
    bool KeepScrollback,
    string? RestoredFrom,
    bool OfferRerun);

public sealed record TerminalKeepScrollbackRequest(string Id, bool Keep);

/// <param name="Profile">A shell profile id from terminals.profiles.</param>
/// <param name="Folder">An absolute folder; empty opens in the user's home folder.</param>
/// <param name="TaskId">The task this terminal belongs to, if any.</param>
/// <param name="StartupCommand">Typed into the shell once it starts, e.g. <c>pnpm dev</c>.</param>
/// <param name="KeepScrollback">Save its scrollback, encrypted, so it comes back after a quit.</param>
public sealed record OpenTerminalRequest(string Profile, string Folder, string? TaskId, string? StartupCommand, int Cols, int Rows, bool KeepScrollback);

public sealed record TerminalIdRequest(string Id);

public sealed record TerminalInputRequest(string Id, string Data);

public sealed record TerminalResizeRequest(string Id, int Cols, int Rows);

/// <param name="TaskId">Null detaches it from its task.</param>
public sealed record TerminalSetTaskRequest(string Id, string? TaskId);

/// <param name="Data">The scrollback so far.</param>
/// <param name="Seq">The number of the last chunk it includes.</param>
public sealed record TerminalSnapshot(string Id, string Data, long Seq);

public sealed record TerminalOutputEvent(string Id, string Data, long Seq);
