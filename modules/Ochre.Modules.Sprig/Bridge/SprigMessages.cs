using System.Text.Json.Serialization;
using Ochre.Core.Bridge;

namespace Ochre.Modules.Sprig.Bridge;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Empty))]
[JsonSerializable(typeof(SprigTaskRequest))]
[JsonSerializable(typeof(SprigTaskStatus))]
[JsonSerializable(typeof(SprigActRequest))]
[JsonSerializable(typeof(SprigActResult))]
[JsonSerializable(typeof(SprigLogRequest))]
[JsonSerializable(typeof(SprigLogDto))]
[JsonSerializable(typeof(SprigWorkspaceRequest))]
internal sealed partial class SprigJson : JsonSerializerContext;

// Bridge shapes for the sprig module: the workspaces a task's folders are in, their apps, and what can be done to
// them. The renderer names a task and a workspace key; the host only acts on workspaces that task's folders are in.

public sealed record SprigTaskRequest(string TaskId);

/// <param name="Feed">connecting, live, reconnecting or notInstalled.</param>
/// <param name="HostRunning">Sprig's host is up; without it nothing runs.</param>
/// <param name="Workspaces">The workspaces the task's repos and worktrees are in.</param>
public sealed record SprigTaskStatus(string Feed, bool HostRunning, IReadOnlyList<SprigWorkspaceDto> Workspaces);

/// <param name="Key"><c>orchestration/name</c>.</param>
/// <param name="Label">Its branch, or its name.</param>
/// <param name="State">As shown: free, starting, running, paused, stopped, failed or unknown.</param>
/// <param name="StateText">Sprig's own word, for a state Ochre doesn't know.</param>
/// <param name="Activity">none, starting or stopping.</param>
/// <param name="Ready">Apps up, out of <paramref name="Of"/> started.</param>
/// <param name="Folders">The task's folders inside it.</param>
public sealed record SprigWorkspaceDto(
    string Key, string Label, string State, string StateText, bool InTrouble, string Activity, int Ready, int Of,
    string? Error, IReadOnlyList<string> Folders, IReadOnlyList<SprigAppDto> Apps);

/// <param name="Name"><c>repo/app</c>.</param>
/// <param name="ShortName">The part people call it.</param>
/// <param name="State">building, waiting, starting, ready, running, completed, exited, restarting, stopping, stopped, failed or off.</param>
/// <param name="Doing">What it's busy with, e.g. "installing packages".</param>
/// <param name="HasLog">Sprig keeps a log file for it, so sprig.log can show it.</param>
public sealed record SprigAppDto(string Name, string ShortName, string State, int? Port, int? Exit, string? Error, string? Doing, bool HasLog);

/// <param name="Action">start, stop or restart.</param>
/// <param name="App">One app (<c>repo/app</c>), or null for the whole workspace.</param>
public sealed record SprigActRequest(string TaskId, string Key, string Action, string? App);

/// <param name="Code">Sprig's error code (untrusted, inputs-needed, docker-unavailable ...).</param>
/// <param name="NeedsSprig">Only sprig's own window can sort it out (approve a definition, give inputs).</param>
public sealed record SprigActResult(bool Ok, string? Code, string? Error, bool NeedsSprig);

public sealed record SprigLogRequest(string TaskId, string Key, string App);

/// <param name="Text">The end of the log.</param>
/// <param name="Truncated">Earlier lines were left out.</param>
public sealed record SprigLogDto(string App, string Path, string Text, bool Truncated);

public sealed record SprigWorkspaceRequest(string TaskId, string Key);
