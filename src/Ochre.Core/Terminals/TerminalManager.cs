using System.Text;
using Ochre.Core.Platform;

namespace Ochre.Core.Terminals;

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
    public Terminal Open(string profileId, string folder, string? taskId, string? startupCommand, int cols, int rows)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == profileId) ?? throw new ArgumentException($"unknown shell '{profileId}'", nameof(profileId));
        var terminal = new Terminal("t-" + Guid.NewGuid().ToString("N")[..8], profile, folder, taskId, startupCommand, ScrollbackChars);
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
