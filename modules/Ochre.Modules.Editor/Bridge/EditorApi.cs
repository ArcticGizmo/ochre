using System.Collections.Concurrent;
using Ochre.Core.Bridge;
using Ochre.Core.Files;
using Ochre.Core.Git;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Editor.Bridge;

/// <summary>
/// The editor's bridge messages (ADR 0015). The renderer is untrusted: every call names a task and a folder the task
/// records, a path is resolved by <see cref="TaskPaths"/>, and a diff's left side is read from git only for a path git
/// itself listed as changed in that folder.
/// </summary>
internal static class EditorApi
{
    private const int MaxWatched = 64;

    public static void Register(BridgeDispatcher bridge, TaskStore tasks, ChangesCache changes, FileWatches watches)
    {
        var j = EditorJson.Default;

        bridge.Handle("editor.list", j.EditorFolderRequest, j.EditorFileListDto, async r =>
        {
            var folder = Folder(tasks, r.TaskId, r.Folder);
            var files = await Task.Run(() => ProjectFileScan.Scan(folder)).ConfigureAwait(false);
            return new EditorFileListDto(folder, files.RelativePaths, files.Git, files.Truncated);
        });

        bridge.Handle("editor.read", j.EditorFileRequest, j.EditorFileDto, async r =>
        {
            var folder = Folder(tasks, r.TaskId, r.Folder);
            var full = Resolve(folder, r.Path);
            return Dto(folder, r.Path, await Task.Run(() => TextFiles.Read(full)).ConfigureAwait(false));
        });

        bridge.Handle("editor.write", j.EditorWriteRequest, j.EditorWriteDto, async r =>
        {
            var folder = Folder(tasks, r.TaskId, r.Folder);
            var full = Resolve(folder, r.Path);
            var result = await Task.Run(() =>
            {
                try
                {
                    return TextFiles.Write(full, r.Text, r.Version);
                }
                catch (InvalidOperationException ex)
                {
                    throw new BridgeRejectException(ex.Message);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new BridgeRejectException("couldn't save: " + ex.Message);
                }
            }).ConfigureAwait(false);
            if (result.Saved) changes.Forget(folder);
            return new EditorWriteDto(result.Saved, result.Version);
        });

        bridge.Handle("editor.head", j.EditorFileRequest, j.EditorFileDto, async r =>
        {
            var folder = Folder(tasks, r.TaskId, r.Folder);
            Resolve(folder, r.Path);
            var read = await Task.Run(() =>
            {
                // Recent enough unless the file only just changed; then ask git again before refusing.
                var list = changes.Get(folder);
                var change = Find(list, r.Path);
                if (change is null)
                {
                    list = changes.Get(folder, fresh: true);
                    change = Find(list, r.Path) ?? throw new BridgeRejectException("that file has no changes since HEAD");
                }
                if (change.Untracked || change.Letter == "A") return new TextFileRead("", "", 0, false, TextFileProblem.None, true);
                return GitFolderChanges.ReadAt(list!.Root, "HEAD", change.OrigRootPath ?? change.RootPath);
            }).ConfigureAwait(false);
            return Dto(folder, r.Path, read);
        });

        bridge.Handle("editor.changes", j.EditorTaskRequest, j.IReadOnlyListEditorFolderChangesDto, async r =>
        {
            var folders = TaskPaths.Folders(TaskDoc(tasks, r.TaskId));
            var lists = await Task.WhenAll(folders.Select(f => Task.Run(() => FolderChanges(f, changes)))).ConfigureAwait(false);
            return (IReadOnlyList<EditorFolderChangesDto>)lists;
        });

        bridge.Handle("editor.locate", j.EditorLocateRequest, j.EditorLocateDto, r =>
        {
            return TaskPaths.Locate(TaskDoc(tasks, r.TaskId), r.Path) is { } found && TaskPaths.TryResolve(found.Folder, found.Relative, out _, out _)
                ? new EditorLocateDto(found.Folder, found.Relative)
                : new EditorLocateDto(null, null);
        });

        bridge.Handle("editor.watch", j.EditorWatchRequest, j.Ack, r =>
        {
            if (r.Files.Count > MaxWatched) throw new BridgeRejectException("too many files");
            var files = r.Files.Select(f =>
            {
                var folder = Folder(tasks, f.TaskId, f.Folder);
                return new WatchedFile(f.TaskId, folder, f.Path, Resolve(folder, f.Path));
            }).ToList();
            watches.Set(bridge, files);
            return new Ack();
        });
    }

    private static TaskDocument TaskDoc(TaskStore tasks, string taskId)
    {
        if (!TaskStore.IsTaskId(taskId)) throw new BridgeRejectException("invalid task id");
        return tasks.Get(taskId) ?? throw new BridgeRejectException("no such task");
    }

    private static string Folder(TaskStore tasks, string taskId, string folder) =>
        TaskPaths.FolderOf(TaskDoc(tasks, taskId), folder) ?? throw new BridgeRejectException("not one of the task's folders");

    private static string Resolve(string folder, string path) =>
        TaskPaths.TryResolve(folder, path, out var full, out var error) ? full : throw new BridgeRejectException(error);

    private static GitFolderChange? Find(GitFolderChangeList? list, string path) =>
        list?.Changes.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.Ordinal));

    private static EditorFolderChangesDto FolderChanges(string folder, ChangesCache changes)
    {
        if (!Directory.Exists(folder)) return new EditorFolderChangesDto(folder, true, false, [], false);
        if (changes.Get(folder) is not { } list) return new EditorFolderChangesDto(folder, false, false, [], false);
        return new EditorFolderChangesDto(
            folder,
            false,
            true,
            list.Changes.Select(c => new EditorChangeDto(c.Path, c.OrigPath, c.Letter, c.Added, c.Removed, c.Binary)).ToList(),
            list.Truncated);
    }

    private static EditorFileDto Dto(string folder, string path, TextFileRead read) => new(
        folder,
        path,
        read.Text,
        read.Version,
        read.Size,
        read.Bom,
        read.Problem switch
        {
            TextFileProblem.Missing => EditorFileProblemDto.Missing,
            TextFileProblem.TooLarge => EditorFileProblemDto.TooLarge,
            TextFileProblem.Binary => EditorFileProblemDto.Binary,
            TextFileProblem.NotUtf8 => EditorFileProblemDto.NotUtf8,
            _ => EditorFileProblemDto.None,
        },
        read.ReadOnly);
}

/// <summary>
/// Keeps each folder's change list for a few seconds: several windows, and the task page beside the Files view, ask
/// for the same folders when focus moves, and one git run answers them all.
/// </summary>
internal sealed class ChangesCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<string, (GitFolderChangeList? List, DateTime At)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public GitFolderChangeList? Get(string folder, bool fresh = false)
    {
        if (!fresh && _entries.TryGetValue(folder, out var hit) && DateTime.UtcNow - hit.At < MaxAge) return hit.List;
        var list = GitFolderChanges.Read(folder);
        _entries[folder] = (list, DateTime.UtcNow);
        return list;
    }

    public void Forget(string folder) => _entries.TryRemove(folder, out _);
}
