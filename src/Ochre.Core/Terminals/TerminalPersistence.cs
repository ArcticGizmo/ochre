namespace Ochre.Core.Terminals;

/// <summary>
/// Keeps the terminal tabs on disk so they come back after a quit (M3 slice 3). The list is saved shortly after any
/// change; scrollback every <see cref="ScrollbackInterval"/> while it changes, and everything once more on quit, so a
/// crash or a shutdown loses at most that interval of output.
/// </summary>
public sealed class TerminalPersistence : IDisposable
{
    public static readonly TimeSpan ScrollbackInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ListDelay = TimeSpan.FromMilliseconds(300);

    private readonly TerminalManager _terminals;
    private readonly TerminalStore _store;
    private readonly AppLog _log;
    private readonly Lock _gate = new();
    private readonly Timer _listTimer;
    private readonly Timer _scrollbackTimer;

    // Scrollback files this session may delete: tabs it restored or opened. Files it never knew about (say from a
    // list it couldn't read) are left alone rather than treated as orphans.
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private bool _listUnreadable;
    private bool _disposed;

    public TerminalPersistence(TerminalManager terminals, TerminalStore store, AppLog log)
    {
        _terminals = terminals;
        _store = store;
        _log = log;
        _listTimer = new Timer(_ => SaveList(), null, Timeout.Infinite, Timeout.Infinite);
        _scrollbackTimer = new Timer(_ => SaveScrollback(), null, ScrollbackInterval, ScrollbackInterval);
        _terminals.Changed += () =>
        {
            lock (_gate)
            {
                if (!_disposed) _listTimer.Change(ListDelay, Timeout.InfiniteTimeSpan);
            }
        };
    }

    /// <summary>Brings back the tabs saved last time. Call once, at start, before any window opens.</summary>
    public int RestoreAll()
    {
        var saved = _store.Load();
        if (saved.ReadFailed)
        {
            // Never replace a list that's there but unreadable; it may only be locked, or fixable by hand.
            _listUnreadable = true;
            _log.Warn("terminals.json could not be read; terminal tabs won't be saved this session");
            return 0;
        }
        var restored = 0;
        foreach (var s in saved.Terminals)
        {
            lock (_gate) _known.Add(s.Id);
            try
            {
                _terminals.Restore(s, s.KeepScrollback ? _store.LoadScrollback(s.Id) : null, saved.SavedAt);
                restored++;
            }
            catch (InvalidOperationException ex)
            {
                _log.Warn($"terminal {s.Id} not restored: {ex.Message}");
            }
        }
        return restored;
    }

    /// <summary>Saves the list and every changed scrollback now. Called on quit, before the shells are closed.</summary>
    public void SaveNow()
    {
        SaveList();
        SaveScrollback();
    }

    private void SaveList()
    {
        lock (_gate)
        {
            if (_disposed || _listUnreadable) return;
            var all = _terminals.All;
            try
            {
                _store.SaveList(all.Select(t => new SavedTerminal(t.Id, t.Profile.Id, t.Folder, t.TaskId, t.StartupCommand, t.Title, t.KeepScrollback)), DateTimeOffset.UtcNow);
                foreach (var t in all) _known.Add(t.Id);
                // A closed tab, or one switched to keep nothing, loses its saved scrollback.
                var keep = all.Where(t => t.KeepScrollback).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var id in _known.Where(id => !keep.Contains(id)).ToList())
                {
                    _store.DeleteScrollback(id);
                    if (all.All(t => t.Id != id)) _known.Remove(id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"terminal list not saved: {ex.Message}");
            }
        }
    }

    private void SaveScrollback()
    {
        lock (_gate)
        {
            if (_disposed || _listUnreadable) return;
            foreach (var (id, text) in _terminals.TakeScrollbackToSave())
            {
                try
                {
                    _store.SaveScrollback(id, text);
                    _known.Add(id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    _log.Warn($"terminal {id} scrollback not saved: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _listTimer.Dispose();
            _scrollbackTimer.Dispose();
        }
    }
}
