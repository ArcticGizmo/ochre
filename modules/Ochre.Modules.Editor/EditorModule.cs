using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Modules.Editor.Bridge;

namespace Ochre.Modules.Editor;

/// <summary>
/// The file editor (M6, ADR 0015): a task's files, read and saved only inside its own folders, what changed in them
/// since HEAD, and word when a file a window has open changes on disk.
/// </summary>
internal sealed class EditorModule : IModule
{
    public string Id => "editor";
    public string Name => "Editor";
    public string Description => "Open, edit and save the files in a task's folders, see what changed since the last commit as diffs, and open files from an agent's tool calls.";
    public bool DefaultEnabled => true;

    public IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("editor.list", typeof(EditorFolderRequest), typeof(EditorFileListDto)),
        new("editor.read", typeof(EditorFileRequest), typeof(EditorFileDto)),
        new("editor.write", typeof(EditorWriteRequest), typeof(EditorWriteDto)),
        new("editor.head", typeof(EditorFileRequest), typeof(EditorFileDto)),
        new("editor.changes", typeof(EditorTaskRequest), typeof(IReadOnlyList<EditorFolderChangesDto>)),
        new("editor.locate", typeof(EditorLocateRequest), typeof(EditorLocateDto)),
        new("editor.watch", typeof(EditorWatchRequest), typeof(Ack)),
    ];

    public IReadOnlyList<EventContract> Events { get; } =
    [
        new("editor.changed", typeof(EditorChangedEvent)),
    ];

    public IDisposable Start(IModuleContext context)
    {
        var changes = new ChangesCache();
        var watches = new FileWatches(
            (f, version) =>
            {
                changes.Forget(f.Folder);
                context.Broadcast("editor.changed", new EditorChangedEvent(f.TaskId, f.Folder, f.Path, version), EditorJson.Default.EditorChangedEvent);
            },
            context.Log);
        context.AddBridge(bridge => EditorApi.Register(bridge, context.Tasks, changes, watches));
        context.OnWindowClosed(watches.Remove);
        return watches;
    }
}
