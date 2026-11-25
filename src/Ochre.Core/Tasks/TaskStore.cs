using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ochre.Core.Tasks;

/// <summary>
/// The only writer of task files (ADR 0001). Every change is a read-modify-write of the file on disk under a
/// per-task lock, so edits made in an editor are picked up rather than overwritten, and two agents never race on
/// one file. Writes are atomic (temp file, then replace) and append a log line naming the author.
/// </summary>
public sealed partial class TaskStore : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly AppLog _log;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Timer> _pendingReloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _createGate = new(1, 1);
    private FileSystemWatcher? _watcher;

    private sealed record Entry(TaskDocument Document, string Text);

    public TaskStore(string folder, AppLog log, TimeProvider? clock = null)
    {
        _folder = folder;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>A task was created, changed (by Ochre or by an external edit) or deleted. Raised on a pool thread.</summary>
    public event Action<string>? Changed;

    [GeneratedRegex(@"^T-\d{4,}$", RegexOptions.IgnoreCase)]
    private static partial Regex TaskId();

    public static bool IsTaskId(string id) => TaskId().IsMatch(id);

    /// <summary>Loads every task and starts watching for external edits. Never throws for a bad file.</summary>
    public void Start()
    {
        Directory.CreateDirectory(_folder);
        foreach (var path in Directory.EnumerateFiles(_folder, "T-*.md"))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (IsTaskId(id)) TryLoad(id, raise: false);
        }

        _watcher = new FileSystemWatcher(_folder, "T-*.md") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        _watcher.Changed += (_, e) => ScheduleReload(e.Name);
        _watcher.Created += (_, e) => ScheduleReload(e.Name);
        _watcher.Deleted += (_, e) => ScheduleReload(e.Name);
        _watcher.Renamed += (_, e) =>
        {
            ScheduleReload(e.OldName);
            ScheduleReload(e.Name);
        };
        _watcher.Error += (_, e) => _log.Warn($"task folder watcher error: {e.GetException().Message}");
        _watcher.EnableRaisingEvents = true;
    }

    public IReadOnlyList<TaskSummary> List() =>
        _cache.Select(kv => kv.Value.Document.ToSummary(kv.Key)).OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();

    public TaskDocument? Get(string id) => _cache.TryGetValue(id, out var e) ? TaskDocument.Parse(e.Text) : null;

    public async Task<string> CreateAsync(string title, string author, CancellationToken ct = default)
    {
        await _createGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_folder);
            var next = Directory.EnumerateFiles(_folder, "T-*.md")
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Where(IsTaskId)
                .Select(id => int.Parse(id.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .Max() + 1;

            while (true)
            {
                var id = $"T-{next:D4}";
                var now = _clock.GetUtcNow();
                var doc = TaskDocument.CreateNew(id, title, now);
                doc.AppendLog(now, author, "created");
                var text = doc.Serialize();
                try
                {
                    // CreateNew fails rather than overwrite if something else claimed the id in the meantime.
                    await using (var stream = new FileStream(PathFor(id), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await using (var writer = new StreamWriter(stream, Utf8))
                        await writer.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
                }
                catch (IOException) when (File.Exists(PathFor(id)))
                {
                    next++;
                    continue;
                }
                _cache[id] = new Entry(doc, text);
                _log.Info($"task {id} created by {author}");
                Changed?.Invoke(id);
                return id;
            }
        }
        finally
        {
            _createGate.Release();
        }
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to the current file contents. The mutation returns a log message, or null
    /// to make no change. Throws <see cref="KeyNotFoundException"/> for an unknown task and
    /// <see cref="TaskFormatException"/> if the file on disk can't be parsed.
    /// </summary>
    public async Task<TaskDocument> UpdateAsync(string id, string author, Func<TaskDocument, string?> mutate, CancellationToken ct = default)
    {
        if (!IsTaskId(id)) throw new KeyNotFoundException($"no task '{id}'");
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path)) throw new KeyNotFoundException($"no task '{id}'");

            // Always start from disk: an edit made in an editor since the last read must not be lost.
            var doc = TaskDocument.Parse(await ReadSharedAsync(path, ct).ConfigureAwait(false));
            var message = mutate(doc);
            if (message is null) return doc;

            var now = _clock.GetUtcNow();
            doc.EnsureItemIds();
            if (!string.Equals(doc.Id, id, StringComparison.Ordinal)) doc.Id = id;
            doc.Updated = now;
            doc.AppendLog(now, author, message);
            var text = doc.Serialize();

            await WriteAtomicAsync(path, text, ct).ConfigureAwait(false);
            _cache[id] = new Entry(doc, text);
            _log.Info($"task {id} updated by {author}");
            Changed?.Invoke(id);
            return doc;
        }
        finally
        {
            gate.Release();
        }
    }

    private string PathFor(string id) => Path.Combine(_folder, id.ToUpperInvariant() + ".md");

    private static async Task<string> ReadSharedAsync(string path, CancellationToken ct)
    {
        // Editors and other tools may hold the file open; share everything and read what's there.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private async Task WriteAtomicAsync(string path, string text, CancellationToken ct)
    {
        var temp = Path.Combine(_folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temp, text, Utf8, ct).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
            {
                // An editor or scanner briefly holding the file without delete sharing.
                await Task.Delay(50 * attempt, ct).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
    }

    private void ScheduleReload(string? name)
    {
        if (name is null) return;
        var id = Path.GetFileNameWithoutExtension(name);
        if (!IsTaskId(id)) return;
        // Editors write in bursts (truncate, write, rename); wait for the burst to settle before reading.
        var timer = _pendingReloads.GetOrAdd(id, key => new Timer(_ => Reload(key), null, Timeout.Infinite, Timeout.Infinite));
        timer.Change(200, Timeout.Infinite);
    }

    private void Reload(string id)
    {
        if (_pendingReloads.TryRemove(id, out var timer)) timer.Dispose();
        TryLoad(id, raise: true);
    }

    private void TryLoad(string id, bool raise)
    {
        var path = PathFor(id);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    if (_cache.TryRemove(id, out _) && raise) Changed?.Invoke(id);
                    return;
                }
                var text = ReadSharedAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                // Our own writes come back through the watcher; they're already cached.
                if (_cache.TryGetValue(id, out var existing) && existing.Text == text) return;
                _cache[id] = new Entry(TaskDocument.Parse(text), text);
                if (raise) Changed?.Invoke(id);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100);
            }
            catch (IOException ex)
            {
                _log.Warn($"task {id} could not be read: {ex.Message}");
                return;
            }
            catch (TaskFormatException ex)
            {
                // Probably mid-edit; keep the last good version and try again on the next change.
                _log.Warn($"task {id} is not valid: {ex.Message}");
                return;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var timer in _pendingReloads.Values) timer.Dispose();
        _createGate.Dispose();
        foreach (var gate in _gates.Values) gate.Dispose();
    }
}
