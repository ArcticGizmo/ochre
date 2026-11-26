using System.Text.Json.Serialization;

namespace Ochre.Modules.Git.Bridge;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GitStatusRequest))]
[JsonSerializable(typeof(IReadOnlyList<GitFolderStatus>))]
[JsonSerializable(typeof(GitChangedEvent))]
[JsonSerializable(typeof(GraphLoadRequest))]
[JsonSerializable(typeof(GraphPageDto))]
[JsonSerializable(typeof(GraphTokenRequest))]
[JsonSerializable(typeof(GraphMoreDto))]
[JsonSerializable(typeof(GraphCommitRequest))]
[JsonSerializable(typeof(GraphCommitDto))]
[JsonSerializable(typeof(GraphChangesRequest))]
[JsonSerializable(typeof(IReadOnlyList<GraphChangeDto>))]
internal sealed partial class GitJson : JsonSerializerContext;

// Bridge shapes for the git module: the state of each folder a task works in (its repos and its worktrees).

public sealed record GitStatusRequest(string TaskId);

/// <param name="Path">One of the task's repos or worktrees, as the task records it.</param>
/// <param name="Missing">The folder isn't there.</param>
/// <param name="IsRepo">It's inside a git checkout and git answered; the rest is empty otherwise.</param>
/// <param name="Branch">Null when detached.</param>
/// <param name="Upstream">The branch it tracks, e.g. <c>origin/main</c>; null when none.</param>
/// <param name="Changed">Uncommitted and untracked paths.</param>
public sealed record GitFolderStatus(string Path, bool Missing, bool IsRepo, string? Branch, string? Upstream, int Ahead, int Behind, int Changed);

/// <summary>An agent on the task finished a turn, so its folders may have changed: fetch git.status again.</summary>
public sealed record GitChangedEvent(string TaskId);
