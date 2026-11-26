using System.Text;
using Ochre.Core;
using Ochre.Core.Platform;

namespace Ochre.Modules.Terminals;

/// <summary>One terminal tab: a shell in a pseudoconsole, and the scrollback every window replays (ADR 0011).</summary>
public sealed class Terminal
{
    internal Terminal(string id, ShellProfile profile, string folder, string? taskId, string? startupCommand, int scrollbackChars)
    {
        Id = id;
        Profile = profile;
        Folder = folder;
        TaskId = taskId;
        StartupCommand = startupCommand;
        OpenedAt = DateTimeOffset.UtcNow;
        Scrollback = new ScrollbackRing(scrollbackChars);
    }

    public string Id { get; }
    public ShellProfile Profile { get; }

    /// <summary>The folder it opened in, then the last one the shell reported (OSC 7 or 9;9) if it reports any.</summary>
    public string Folder { get; internal set; }

    public string? TaskId { get; internal set; }

    /// <summary>Typed into the shell when it first started.</summary>
    public string? StartupCommand { get; }

    /// <summary>The title the program set, cleaned for display. Never trusted for a decision (security threat 7).</summary>
    public string? Title { get; internal set; }

    public DateTimeOffset OpenedAt { get; }
    public bool Running { get; internal set; }
    public int? ExitCode { get; internal set; }

    /// <summary>Save its scrollback (encrypted) for the next start; off for a tab that will show secrets.</summary>
    public bool KeepScrollback { get; internal set; } = true;

    /// <summary>When Ochre last saved it, for a tab brought back after a restart; null once the user dismisses that.</summary>
    public DateTimeOffset? RestoredFrom { get; internal set; }

    /// <summary>Brought back with a startup command that hasn't been run again. It never reruns by itself.</summary>
    public bool OfferRerun { get; internal set; }

    internal Lock Gate { get; } = new();
    internal IPseudoConsole? Console { get; set; }
    internal TerminalOutputFilter Filter { get; set; } = new();
    internal StringBuilder Pending { get; } = new();
    internal bool FlushScheduled { get; set; }
    internal Timer? FlushTimer { get; set; }
    internal ScrollbackRing Scrollback { get; }
    internal long Seq { get; set; }
    internal int Cols { get; set; }
    internal int Rows { get; set; }
    internal bool Closed { get; set; }
    internal bool MetadataChanged { get; set; }
    internal bool ScrollbackDirty { get; set; }
}

/// <summary>
/// Owns every terminal (ADR 0008: windows are views). Output is filtered, coalesced on a short timer, kept in the
/// terminal's scrollback and raised with a sequence number, so a window that joins late takes the scrollback and
/// applies only chunks after it.
/// </summary>
public sealed class TerminalManager : IDisposable
{
    public static readonly TimeSpan CoalesceInterval = TimeSpan.FromMilliseconds(16);
    public const int ScrollbackChars = 1_000_000;
    public const int MaxTitle = 120;

    private readonly ITerminalPlatform _platform;
    private readonly AppLog _log;
    private readonly Lock _gate = new();
    private readonly List<Terminal> _terminals = [];
    private IReadOnlyList<ShellProfile>? _profiles;

    public TerminalManager(ITerminalPlatform platform, AppLog log)
    {
        _platform = platform;
        _log = log;
    }

    /// <summary>A flushed chunk of a terminal's output and its sequence number. Raised under the terminal's gate, so in order.</summary>
    public event Action<Terminal, string, long>? Output;

    /// <summary>A terminal opened, closed, exited, or changed its title, folder or task.</summary>
    public event Action? Changed;

    public IReadOnlyList<ShellProfile> Profiles
    {
        get
        {
            lock (_gate) return _profiles ??= _platform.DetectShells();
        }
    }

    public IReadOnlyList<Terminal> All
    {
        get
        {
            lock (_gate) return [.. _terminals];
        }
    }

    public Terminal? Get(string id)
    {
        lock (_gate) return _terminals.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>Opens a terminal. Callers validate the folder, task and command; this only checks the profile exists.</summary>
    public Terminal Open(string profileId, string folder, string? taskId, string? startupCommand, int cols, int rows, bool keepScrollback = true)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == profileId) ?? throw new ArgumentException($"unknown shell '{profileId}'", nameof(profileId));
        var terminal = new Terminal("t-" + Guid.NewGuid().ToString("N")[..8], profile, folder, taskId, startupCommand, ScrollbackChars) { KeepScrollback = keepScrollback };
        terminal.FlushTimer = new Timer(_ => Flush(terminal), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            Start(terminal, cols, rows);
        }
        catch
        {
            terminal.FlushTimer.Dispose();
            throw;
        }
        lock (_gate) _terminals.Add(terminal);
        if (startupCommand is not null) terminal.Console?.Write(startupCommand + "\r");
        _log.Info($"terminal {terminal.Id} opened ({profile.Id})");
        Changed?.Invoke();
        return terminal;
    }

    /// <summary>
    /// Brings back a tab saved before Ochre last quit: its old scrollback, a divider, then a fresh shell in its last
    /// known folder. The startup command is offered, not run (explicit over magic). A shell that won't start leaves
    /// the tab readable, exited, so the scrollback isn't lost.
    /// </summary>
    public Terminal Restore(SavedTerminal saved, string? scrollback, DateTimeOffset? savedAt, int cols = 120, int rows = 30)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == saved.ProfileId) ?? Profiles.FirstOrDefault()
            ?? throw new InvalidOperationException("no shell to restore a terminal with");
        var folder = Directory.Exists(saved.Folder) ? saved.Folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var terminal = new Terminal(saved.Id, profile, folder, saved.TaskId, saved.StartupCommand, ScrollbackChars)
        {
            Title = saved.Title,
            KeepScrollback = saved.KeepScrollback,
            RestoredFrom = savedAt ?? DateTimeOffset.UtcNow,
            OfferRerun = saved.StartupCommand is not null,
        };
        terminal.FlushTimer = new Timer(_ => Flush(terminal), null, Timeout.Infinite, Timeout.Infinite);
        terminal.Scrollback.Append((scrollback ?? "") + Divider(savedAt));
        terminal.Seq = 1;
        lock (_gate) _terminals.Add(terminal);
        try
        {
            Start(terminal, cols, rows);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            lock (terminal.Gate)
            {
                terminal.Running = false;
                terminal.ExitCode = -1;
                terminal.Pending.Append($"\u001b[2m[the shell could not be started: {ex.Message}]\u001b[0m\r\n");
                FlushLocked(terminal);
            }
            _log.Warn($"terminal {terminal.Id} restored without a shell: {ex.Message}");
        }
        _log.Info($"terminal {terminal.Id} restored ({profile.Id})");
        Changed?.Invoke();
        return terminal;
    }

    // Puts the screen back to normal whatever the old program left on (alternate screen, mouse reporting, bracketed
    // paste, hidden cursor), then a divider, so a restored tab is always readable. ConPTY places its cursor by
    // absolute position, so the old stream can end mid-screen: clearing first (which the UI's xterm turns into a push
    // to scrollback) and homing the cursor keeps the divider from overwriting old lines.
    private static string Divider(DateTimeOffset? savedAt)
    {
        const string reset = "\u001b[0m\u001b[?1049l\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l\u001b[?2004l\u001b[?1l\u001b[?25h\u001b[2J\u001b[H";
        var when = savedAt is { } at ? $" · saved {at.ToLocalTime():d MMM HH:mm}" : "";
        return $"{reset}\u001b[38;2;143;179;224m──── restored{when} · new shell {DateTimeOffset.Now:d MMM HH:mm} ────\u001b[0m\r\n";
    }

    /// <summary>Types the startup command again in a restored tab, once the user asks.</summary>
    public void Rerun(Terminal terminal)
    {
        IPseudoConsole? console;
        lock (terminal.Gate)
        {
            if (!terminal.OfferRerun || terminal.StartupCommand is null) throw new InvalidOperationException("there's no command to run again");
            console = terminal.Running ? terminal.Console : null;
            if (console is null) throw new InvalidOperationException("that terminal's shell has exited");
            terminal.OfferRerun = false;
            terminal.RestoredFrom = null;
        }
        console.Write(terminal.StartupCommand + "\r");
        Changed?.Invoke();
    }

    public void DismissRestore(Terminal terminal)
    {
        terminal.OfferRerun = false;
        terminal.RestoredFrom = null;
        Changed?.Invoke();
    }

    public void SetKeepScrollback(Terminal terminal, bool keep)
    {
        lock (terminal.Gate)
        {
            terminal.KeepScrollback = keep;
            terminal.ScrollbackDirty = keep;
        }
        Changed?.Invoke();
    }

    /// <summary>The scrollback of each tab that keeps it and has changed since the last call.</summary>
    public IReadOnlyList<(string Id, string Text)> TakeScrollbackToSave()
    {
        var result = new List<(string, string)>();
        foreach (var t in All)
        {
            lock (t.Gate)
            {
                if (t.Closed || !t.KeepScrollback || !t.ScrollbackDirty) continue;
                t.ScrollbackDirty = false;
                result.Add((t.Id, t.Scrollback.Text()));
            }
        }
        return result;
    }

    /// <summary>Starts a fresh shell in a terminal whose shell has exited, in its last known folder.</summary>
    public void Restart(Terminal terminal)
    {
        lock (terminal.Gate)
        {
            if (terminal.Closed) throw new InvalidOperationException("that terminal is closed");
            if (terminal.Running) throw new InvalidOperationException("that terminal is still running");
        }
        var folder = Directory.Exists(terminal.Folder) ? terminal.Folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        terminal.Folder = folder;
        Start(terminal, terminal.Cols, terminal.Rows);
        _log.Info($"terminal {terminal.Id} restarted");
        Changed?.Invoke();
    }

    private void Start(Terminal terminal, int cols, int rows)
    {
        var filter = new TerminalOutputFilter();
        filter.TitleChanged += title =>
        {
            terminal.Title = CleanTitle(title);
            terminal.MetadataChanged = true;
        };
        filter.FolderChanged += folder =>
        {
            if (folder == terminal.Folder) return;
            terminal.Folder = folder;
            terminal.MetadataChanged = true;
        };
        lock (terminal.Gate)
        {
            terminal.Filter = filter;
            terminal.Cols = cols;
            terminal.Rows = rows;
            // The callbacks can fire before Start returns, so they wait on the gate until Console is set.
            terminal.Console = _platform.Start(terminal.Profile, terminal.Folder, cols, rows, chunk => OnOutput(terminal, filter, chunk), code => OnExited(terminal, filter, code));
            terminal.Running = true;
            terminal.ExitCode = null;
        }
    }

    public void Input(Terminal terminal, string data)
    {
        IPseudoConsole? console;
        lock (terminal.Gate) console = terminal.Running ? terminal.Console : null;
        if (console is null) throw new InvalidOperationException("that terminal's shell has exited");
        console.Write(data);
    }

    public void Resize(Terminal terminal, int cols, int rows)
    {
        IPseudoConsole? console;
        lock (terminal.Gate)
        {
            if (terminal.Cols == cols && terminal.Rows == rows) return;
            terminal.Cols = cols;
            terminal.Rows = rows;
            console = terminal.Running ? terminal.Console : null;
        }
        console?.Resize(cols, rows);
    }

    public void SetTask(Terminal terminal, string? taskId)
    {
        terminal.TaskId = taskId;
        Changed?.Invoke();
    }

    /// <summary>The scrollback so far and the sequence number of its last chunk.</summary>
    public (string Text, long Seq) Snapshot(Terminal terminal)
    {
        lock (terminal.Gate) return (terminal.Scrollback.Text(), terminal.Seq);
    }

    /// <summary>Ends the shell (and what runs in its console) and removes the tab.</summary>
    public void Close(Terminal terminal)
    {
        IPseudoConsole? console;
        lock (terminal.Gate)
        {
            if (terminal.Closed) return;
            terminal.Closed = true;
            terminal.Running = false;
            console = terminal.Console;
            terminal.Console = null;
            terminal.FlushTimer?.Dispose();
        }
        lock (_gate) _terminals.Remove(terminal);
        // Closing a pseudoconsole can wait on its conhost; keep that off the caller's (UI) thread.
        if (console is not null) _ = Task.Run(console.Dispose);
        _log.Info($"terminal {terminal.Id} closed");
        Changed?.Invoke();
    }

    private void OnOutput(Terminal terminal, TerminalOutputFilter filter, string chunk)
    {
        bool changed;
        lock (terminal.Gate)
        {
            if (terminal.Closed || terminal.Filter != filter) return;
            var text = filter.Process(chunk);
            if (text.Length > 0)
            {
                terminal.Pending.Append(text);
                if (!terminal.FlushScheduled)
                {
                    terminal.FlushScheduled = true;
                    terminal.FlushTimer?.Change(CoalesceInterval, Timeout.InfiniteTimeSpan);
                }
            }
            changed = terminal.MetadataChanged;
            terminal.MetadataChanged = false;
        }
        if (changed) Changed?.Invoke();
    }

    private void OnExited(Terminal terminal, TerminalOutputFilter filter, int code)
    {
        lock (terminal.Gate)
        {
            if (terminal.Closed || terminal.Filter != filter) return;
            terminal.Pending.Append(filter.Flush());
            // Written into the stream so the scrollback, late windows and a restored tab all show it.
            terminal.Pending.Append($"\x1b[0m\r\n\x1b[2m[shell exited with code {code}]\x1b[0m\r\n");
            terminal.Running = false;
            terminal.ExitCode = code;
            terminal.Console = null;
            FlushLocked(terminal);
        }
        _log.Info($"terminal {terminal.Id} shell exited ({code})");
        Changed?.Invoke();
    }

    private void Flush(Terminal terminal)
    {
        lock (terminal.Gate)
        {
            if (!terminal.Closed) FlushLocked(terminal);
        }
    }

    private void FlushLocked(Terminal terminal)
    {
        terminal.FlushScheduled = false;
        if (terminal.Pending.Length == 0) return;
        var text = terminal.Pending.ToString();
        terminal.Pending.Clear();
        terminal.Seq++;
        terminal.Scrollback.Append(text);
        terminal.ScrollbackDirty = true;
        try
        {
            Output?.Invoke(terminal, text, terminal.Seq);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // A window closing mid-post; the others still got it, and this one rereads the scrollback when it returns.
            _log.Warn($"terminal {terminal.Id} output not delivered: {ex.GetType().Name}");
        }
    }

    internal static string? CleanTitle(string title)
    {
        var clean = new string(title.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxTitle) clean = clean[..MaxTitle];
        return clean.Length == 0 ? null : clean;
    }

    public void Dispose()
    {
        foreach (var t in All)
        {
            IPseudoConsole? console;
            lock (t.Gate)
            {
                t.Closed = true;
                console = t.Console;
                t.Console = null;
                t.FlushTimer?.Dispose();
            }
            try
            {
                console?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Warn($"terminal {t.Id} did not close cleanly: {ex.Message}");
            }
        }
        lock (_gate) _terminals.Clear();
    }
}
