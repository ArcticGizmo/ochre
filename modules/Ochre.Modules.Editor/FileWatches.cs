using Ochre.Core;
using Ochre.Core.Files;

namespace Ochre.Modules.Editor;

/// <param name="FullPath">Resolved through <see cref="TaskPaths"/> when the window asked.</param>
internal sealed record WatchedFile(string TaskId, string Folder, string Path, string FullPath);

/// <summary>
/// Tells windows when a file one of them has open changes on disk, usually an agent's edit. Each window says which
/// files it has open; one recursive watcher runs per folder that holds any of them, and only those files raise
/// anything. A burst of writes (an agent's save is often several) settles for <see cref="Settle"/> before the file's
/// new version is read.
/// </summary>
internal sealed class FileWatches : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);

    private readonly Action<WatchedFile, string> _changed;
    private readonly AppLog _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<object, IReadOnlyList<WatchedFile>> _byWindow = [];
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<WatchedFile>> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Timer> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <param name="changed">Called once per watched file with its version now (empty when gone), off the UI thread.</param>
    public FileWatches(Action<WatchedFile, string> changed, AppLog log)
    {
        _changed = changed;
        _log = log;
    }

    public void Set(object window, IReadOnlyList<WatchedFile> files)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (files.Count == 0) _byWindow.Remove(window);
            else _byWindow[window] = files;
            Rebuild();
        }
    }

    public void Remove(object window)
    {
        lock (_gate)
        {
            if (_disposed || !_byWindow.Remove(window)) return;
            Rebuild();
        }
    }

    // Under the gate.
    private void Rebuild()
    {
        var byPath = new Dictionary<string, List<WatchedFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _byWindow.Values.SelectMany(v => v))
        {
            if (!byPath.TryGetValue(f.FullPath, out var list)) byPath[f.FullPath] = list = [];
            if (!list.Any(w => w.TaskId == f.TaskId && string.Equals(w.Folder, f.Folder, StringComparison.OrdinalIgnoreCase) && w.Path == f.Path)) list.Add(f);
        }
        _byPath = byPath;

        var folders = byPath.Values.SelectMany(l => l).Select(f => f.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _watchers.Keys.Where(k => !folders.Contains(k)).ToList())
        {
            _watchers[gone].Dispose();
            _watchers.Remove(gone);
        }
        foreach (var folder in folders.Where(f => !_watchers.ContainsKey(f)))
        {
            try
            {
                // The same spelling TaskPaths builds full paths from, so event paths match them.
                var w = new FileSystemWatcher(Path.GetFullPath(folder))
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                w.Changed += (_, e) => Touched(e.FullPath);
                w.Created += (_, e) => Touched(e.FullPath);
                w.Deleted += (_, e) => Touched(e.FullPath);
                w.Renamed += (_, e) =>
                {
                    Touched(e.OldFullPath);
                    Touched(e.FullPath);
                };
                // Events were lost: check every watched file in the folder.
                w.Error += (_, _) => TouchedFolder(folder);
                w.EnableRaisingEvents = true;
                _watchers[folder] = w;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
            {
                _log.Error($"editor: can't watch {folder}", ex);
            }
        }
    }

    private void TouchedFolder(string folder)
    {
        List<string> paths;
        lock (_gate) paths = _byPath.Where(p => p.Value.Any(f => string.Equals(f.Folder, folder, StringComparison.OrdinalIgnoreCase))).Select(p => p.Key).ToList();
        foreach (var p in paths) Touched(p);
    }

    private void Touched(string fullPath)
    {
        lock (_gate)
        {
            if (_disposed || !_byPath.ContainsKey(fullPath)) return;
            if (_pending.TryGetValue(fullPath, out var timer))
            {
                timer.Change(Settle, Timeout.InfiniteTimeSpan);
                return;
            }
            _pending[fullPath] = new Timer(_ => Fire(fullPath), null, Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(string fullPath)
    {
        List<WatchedFile>? files;
        lock (_gate)
        {
            if (_pending.Remove(fullPath, out var timer)) timer.Dispose();
            if (_disposed || !_byPath.TryGetValue(fullPath, out files)) return;
            files = [.. files];
        }
        string version;
        try
        {
            version = TextFiles.Read(fullPath).Version;
            lock (_gate) _retries.Remove(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A writer holding it without sharing: look again shortly, a few times.
            bool again;
            lock (_gate)
            {
                var n = _retries.GetValueOrDefault(fullPath) + 1;
                again = n <= MaxRetries;
                if (again) _retries[fullPath] = n;
                else _retries.Remove(fullPath);
            }
            if (again) Touched(fullPath);
            return;
        }
        foreach (var f in files) _changed(f, version);
    }

    private const int MaxRetries = 10;
    private readonly Dictionary<string, int> _retries = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var w in _watchers.Values) w.Dispose();
            _watchers.Clear();
            foreach (var t in _pending.Values) t.Dispose();
            _pending.Clear();
            _byWindow.Clear();
            _byPath.Clear();
        }
    }
}
