using System.Text.Json.Serialization;

namespace Ochre.Modules.Git.Bridge;

// Bridge shapes for the git graph (M4 slice 5). A load returns a token; paging, commit details and a worktree's
// changes name that token, so git only ever runs in the repo a task folder opened and on commits it showed.

[JsonConverter(typeof(JsonStringEnumConverter<GraphScopeDto>))]
public enum GraphScopeDto
{
    /// <summary>Every branch, remote branch, tag and worktree.</summary>
    [JsonStringEnumMemberName("all")]
    All,

    /// <summary>The trunk plus whatever each worktree has checked out.</summary>
    [JsonStringEnumMemberName("worktrees")]
    Worktrees,

    /// <summary>The folder's branch and its base, back to where they split.</summary>
    [JsonStringEnumMemberName("branch")]
    Branch,
}

[JsonConverter(typeof(JsonStringEnumConverter<GraphRowKindDto>))]
public enum GraphRowKindDto
{
    [JsonStringEnumMemberName("commit")]
    Commit,

    /// <summary>A worktree's uncommitted changes; <c>Hash</c> is <c>wip:</c> and its path.</summary>
    [JsonStringEnumMemberName("wip")]
    Wip,

    /// <summary>A stash entry; <c>Hash</c> is <c>stash:</c> and its commit.</summary>
    [JsonStringEnumMemberName("stash")]
    Stash,
}

[JsonConverter(typeof(JsonStringEnumConverter<GraphRefKindDto>))]
public enum GraphRefKindDto
{
    [JsonStringEnumMemberName("local")]
    Local,

    [JsonStringEnumMemberName("remote")]
    Remote,

    [JsonStringEnumMemberName("tag")]
    Tag,

    [JsonStringEnumMemberName("stash")]
    Stash,
}

/// <param name="TaskId">The task whose folder it is.</param>
/// <param name="Folder">One of the task's repos or worktrees.</param>
/// <param name="Base">For <see cref="GraphScopeDto.Branch"/>: one of the base candidates, "" for none, null to let Ochre pick.</param>
public sealed record GraphLoadRequest(string TaskId, string Folder, GraphScopeDto Scope, string? Base);

/// <param name="Up">The edges in the band above the row, four numbers each: from lane, to lane, colour (a lane index)
/// and 1 when it's a dashed tether (a worktree's changes or a stash to its commit), else 0.</param>
public sealed record GraphRowDto(string Hash, GraphRowKindDto Kind, string Subject, string Author, long Time, int Lane, bool Merge, IReadOnlyList<int> Up);

/// <param name="Name">What the pill shows: <c>main</c>, <c>origin/main</c>, <c>v0.4.1</c>, <c>stash@{0}</c>.</param>
/// <param name="Target">The row hash it points at.</param>
public sealed record GraphRefDto(string Name, GraphRefKindDto Kind, string Target, string? Upstream, int Ahead, int Behind, bool UpstreamGone);

/// <param name="Head">The commit it has checked out; null for an unborn branch.</param>
/// <param name="Current">The folder the graph was opened on is in this worktree.</param>
public sealed record GraphWorktreeDto(string Path, string Name, string? Branch, string? Head, int Changes, bool Current);

/// <param name="RowHash">Its row's hash, when it has a row.</param>
/// <param name="Text">The message without git's "On main: " in front.</param>
public sealed record GraphStashDto(string Selector, string Text, string Message, string RowHash, string Base, long Time);

/// <param name="Token">Names this load in graph.more, graph.commit and graph.changes.</param>
/// <param name="Root">The checkout the graph was read from.</param>
/// <param name="Base">For the branch scope, the base it was measured against; null when none.</param>
/// <param name="BaseCandidates">The bases it could be measured against, best first.</param>
/// <param name="Ahead">With a base: commits HEAD has that the base doesn't.</param>
/// <param name="Behind">With a base: commits the base has that HEAD doesn't.</param>
/// <param name="OpenEdges">Lanes with a line leaving the last row towards commits not loaded yet.</param>
/// <param name="Error">Why there's no graph (not a git checkout), else null.</param>
public sealed record GraphPageDto(
    string Token,
    string Root,
    GraphScopeDto Scope,
    string? Base,
    IReadOnlyList<string> BaseCandidates,
    int? Ahead,
    int? Behind,
    IReadOnlyList<GraphRowDto> Rows,
    IReadOnlyList<GraphRefDto> Refs,
    IReadOnlyList<GraphWorktreeDto> Worktrees,
    IReadOnlyList<GraphStashDto> Stashes,
    int Width,
    bool HasMore,
    IReadOnlyList<int> OpenEdges,
    string? Error);

public sealed record GraphTokenRequest(string Token);

/// <param name="Rows">The rows to add below the ones already shown.</param>
public sealed record GraphMoreDto(IReadOnlyList<GraphRowDto> Rows, int Width, bool HasMore, IReadOnlyList<int> OpenEdges);

public sealed record GraphCommitRequest(string Token, string Hash);

/// <param name="Body">The whole message, subject included.</param>
public sealed record GraphCommitDto(string Hash, string Author, string Email, long Time, IReadOnlyList<string> Parents, string Body);

/// <param name="Path">One of the graph's worktrees.</param>
public sealed record GraphChangesRequest(string Token, string Path);

/// <param name="Kind">One letter, as git status shows it: M changed, A added, D deleted, R renamed, C copied, T type changed, U conflicted, ? not tracked.</param>
public sealed record GraphChangeDto(string Path, string Kind);
