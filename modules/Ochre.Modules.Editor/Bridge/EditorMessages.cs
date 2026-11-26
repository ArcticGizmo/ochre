using System.Text.Json.Serialization;
using Ochre.Core.Bridge;

namespace Ochre.Modules.Editor.Bridge;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Ack))]
[JsonSerializable(typeof(EditorFolderRequest))]
[JsonSerializable(typeof(EditorFileListDto))]
[JsonSerializable(typeof(EditorFileRequest))]
[JsonSerializable(typeof(EditorFileDto))]
[JsonSerializable(typeof(EditorWriteRequest))]
[JsonSerializable(typeof(EditorWriteDto))]
[JsonSerializable(typeof(EditorTaskRequest))]
[JsonSerializable(typeof(IReadOnlyList<EditorFolderChangesDto>))]
[JsonSerializable(typeof(EditorLocateRequest))]
[JsonSerializable(typeof(EditorLocateDto))]
[JsonSerializable(typeof(EditorWatchRequest))]
[JsonSerializable(typeof(EditorChangedEvent))]
internal sealed partial class EditorJson : JsonSerializerContext;

// Bridge shapes for the editor (M6, ADR 0015). Every call names a task and one of its folders; paths are relative to
// that folder with forward slashes.

[JsonConverter(typeof(JsonStringEnumConverter<EditorFileProblemDto>))]
public enum EditorFileProblemDto
{
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>Not there (deleted, or not in that commit).</summary>
    [JsonStringEnumMemberName("missing")]
    Missing,

    [JsonStringEnumMemberName("tooLarge")]
    TooLarge,

    [JsonStringEnumMemberName("binary")]
    Binary,

    /// <summary>Shown with replacement characters, read-only.</summary>
    [JsonStringEnumMemberName("notUtf8")]
    NotUtf8,
}

/// <param name="Folder">One of the task's repos or worktrees, as the task records it.</param>
public sealed record EditorFolderRequest(string TaskId, string Folder);

/// <param name="Git">Listed by git (honouring .gitignore) rather than by walking the folder.</param>
/// <param name="Truncated">More files than the list holds.</param>
public sealed record EditorFileListDto(string Folder, IReadOnlyList<string> Paths, bool Git, bool Truncated);

public sealed record EditorFileRequest(string TaskId, string Folder, string Path);

/// <param name="Text">Null when it can't be shown; see <paramref name="Problem"/>.</param>
/// <param name="Version">Name it when saving; empty when there's no file, or for text read from git.</param>
/// <param name="ReadOnly">Shown but not saveable (not UTF-8, too big to save, or a commit's text).</param>
public sealed record EditorFileDto(string Folder, string Path, string? Text, string Version, long Size, bool Bom, EditorFileProblemDto Problem, bool ReadOnly);

/// <param name="Version">The version the text was edited from, as read.</param>
public sealed record EditorWriteRequest(string TaskId, string Folder, string Path, string Text, string Version);

/// <param name="Saved">False: the file changed on disk since that version (or went), and nothing was written.</param>
/// <param name="Version">What's on disk now; empty when the file is gone.</param>
public sealed record EditorWriteDto(bool Saved, string Version);

public sealed record EditorTaskRequest(string TaskId);

/// <param name="Status">One letter: M, A, D, R, C, T, U, or ? for untracked.</param>
/// <param name="OrigPath">A rename's old path, when it was in this folder.</param>
/// <param name="Added">Lines added since HEAD; null when unknown.</param>
public sealed record EditorChangeDto(string Path, string? OrigPath, string Status, int? Added, int? Removed, bool Binary);

/// <param name="Git">The folder is in a git checkout; otherwise it has no changes to list.</param>
public sealed record EditorFolderChangesDto(string Folder, bool Missing, bool Git, IReadOnlyList<EditorChangeDto> Changes, bool Truncated);

/// <param name="Path">An absolute path, as an agent's tool call names it.</param>
public sealed record EditorLocateRequest(string TaskId, string Path);

/// <summary>Both null when the path isn't inside any of the task's folders.</summary>
public sealed record EditorLocateDto(string? Folder, string? Path);

/// <param name="Files">Every file this window has open; replaces what it sent before.</param>
public sealed record EditorWatchRequest(IReadOnlyList<EditorFileRequest> Files);

/// <summary>A file some window has open changed on disk.</summary>
/// <param name="Version">Its version now; empty when it's gone. A window that just saved that version can ignore it.</param>
public sealed record EditorChangedEvent(string TaskId, string Folder, string Path, string Version);
