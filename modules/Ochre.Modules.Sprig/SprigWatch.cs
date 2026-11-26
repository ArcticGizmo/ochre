using System.Diagnostics;
using Ochre.Core;

namespace Ochre.Modules.Sprig;

/// <summary>
/// Keeps one <c>sprig watch --json</c> running while the module is on and folds its lines into
/// <see cref="Current"/>. Sprig's own advice for a tool that shows its state all day: read the stream rather than
/// polling commands. The watch never starts sprig's host. If it exits it's started again (sooner the first times);
/// if sprig isn't installed, it's looked for again every minute.
/// </summary>
internal sealed class SprigWatch : IDisposable
{
    private static readonly TimeSpan NotInstalledRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    // Apps report in bursts while a workspace starts; one broadcast covers a burst.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly AppLog _log;
    private readonly SprigStatusFold _fold = new();
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Timer _notify;
    private readonly Thread _thread;
    private Process? _process;

    public SprigWatch(AppLog log)
    {
        _log = log;
        _notify = new Timer(_ => Changed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
        _thread = new Thread(Loop) { IsBackground = true, Name = "sprig watch" };
    }

    /// <summary>What sprig reported, or the connection changed. Raised on a pool thread, once per burst.</summary>
    public event Action? Changed;

    public SprigStatus Current
    {
        get
        {
            lock (_gate) return _fold.Current;
        }
    }

    public SprigFeed Feed { get; private set; } = SprigFeed.Connecting;

    public void Start() => _thread.Start();

    private void Loop()
    {
        var failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            if (SprigCli.Locate() is not { } exe)
            {
                SetFeed(SprigFeed.NotInstalled);
                if (_stop.Token.WaitHandle.WaitOne(NotInstalledRetry)) return;
                continue;
            }

            var sawLines = Watch(exe);
            if (_stop.IsCancellationRequested) return;
            failures = sawLines ? 0 : failures + 1;
            SetFeed(SprigFeed.Reconnecting);
            var wait = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, failures)));
            if (_stop.Token.WaitHandle.WaitOne(wait)) return;
        }
    }

    // Runs one watch process to its end. True when it sent anything, so a watch that keeps dying at once backs off.
    private bool Watch(string exe)
    {
        var any = false;
        try
        {
            using var process = Process.Start(SprigCli.WatchStartInfo(exe));
            if (process is null) return false;
            lock (_gate)
            {
                _process = process;
                _fold.Reset();
            }
            _ = process.StandardError.ReadToEndAsync();
            while (process.StandardOutput.ReadLine() is { } line)
            {
                bool changed;
                lock (_gate) changed = _fold.Apply(line);
                if (!changed) continue;
                any = true;
                Feed = SprigFeed.Live;
                _notify.Change(Settle, Timeout.InfiniteTimeSpan);
            }
            if (!_stop.IsCancellationRequested) _log.Warn($"sprig watch ended (exit {(process.WaitForExit(2000) ? process.ExitCode : -1)})");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            if (!_stop.IsCancellationRequested) _log.Warn($"sprig watch failed: {ex.Message}");
        }
        finally
        {
            lock (_gate) _process = null;
        }
        return any;
    }

    private void SetFeed(SprigFeed feed)
    {
        if (Feed == feed) return;
        Feed = feed;
        _notify.Change(Settle, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _stop.Cancel();
        Process? process;
        lock (_gate) process = _process;
        if (process is not null)
        {
            try
            {
                // Closing stdin is how sprig learns the watcher has gone; the kill covers one that doesn't notice.
                process.StandardInput.Close();
                if (!process.WaitForExit(1000)) process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
        _notify.Dispose();
        _stop.Dispose();
    }
}
