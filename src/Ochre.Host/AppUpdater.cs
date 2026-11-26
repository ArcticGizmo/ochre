using Ochre.Core;
using Ochre.Host.Bridge;
using Velopack;
using Velopack.Sources;

namespace Ochre.Host;

/// <summary>
/// Updates an installed Ochre from its GitHub releases (ADR 0014): checks a minute after start and every six hours,
/// and on request; applying downloads the release, then quits Ochre the normal way (so agents stop and terminals
/// save their scrollback) while Velopack's updater waits to swap the files and start the new version. Nothing is
/// applied without the user asking.
/// </summary>
internal sealed class AppUpdater : IDisposable
{
    public const string RepoUrl = "https://github.com/ArcticGizmo/ochre";

    private readonly UpdateManager? _manager;
    private readonly AppLog _log;
    private readonly Lock _gate = new();
    private Timer? _timer;
    private UpdateInfo? _found;
    private bool _busy;

    public AppUpdater(AppLog log)
    {
        _log = log;
        try
        {
            // OCHRE_UPDATE_FEED points an installed copy at a local releases folder, to try an update before
            // publishing it.
            var feed = Environment.GetEnvironmentVariable("OCHRE_UPDATE_FEED");
            IUpdateSource source = feed is { Length: > 0 } && Path.IsPathFullyQualified(feed) && Directory.Exists(feed)
                ? new SimpleFileSource(new DirectoryInfo(feed))
                : new GithubSource(RepoUrl, null, false);
            var manager = new UpdateManager(source);
            if (manager.IsInstalled && !manager.IsPortable) _manager = manager;
        }
        catch (Exception ex)
        {
            // An update system that can't start leaves Ochre working, just not updating itself.
            log.Error("update manager unavailable", ex);
        }
        State = new UpdateDto(_manager is null ? UpdateStateDto.Unsupported : UpdateStateDto.Idle, AppInfoProvider.Version, null, null, null, null);
    }

    public UpdateDto State { get; private set; }

    /// <summary>Raised from any thread whenever <see cref="State"/> changes.</summary>
    public event Action<UpdateDto>? Changed;

    public void Start()
    {
        if (_manager is null) return;
        _timer = new Timer(_ => _ = CheckAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(6));
    }

    public async Task<UpdateDto> CheckAsync()
    {
        if (_manager is not { } manager) return State;
        lock (_gate)
        {
            if (_busy) return State;
            _busy = true;
        }
        try
        {
            Set(State with { State = UpdateStateDto.Checking, Error = null });
            var found = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            _found = found;
            var checkedAt = DateTimeOffset.Now.ToString("o");
            Set(found is null
                ? State with { State = UpdateStateDto.UpToDate, Available = null, CheckedAt = checkedAt }
                : State with { State = UpdateStateDto.Available, Available = found.TargetFullRelease.Version.ToString(), CheckedAt = checkedAt });
        }
        catch (Exception ex)
        {
            _log.Info($"update check failed: {ex.Message}");
            Set(State with { State = _found is null ? UpdateStateDto.Error : UpdateStateDto.Available, Error = ex.Message });
        }
        finally
        {
            lock (_gate) _busy = false;
        }
        return State;
    }

    /// <summary>Downloads the update found by the last check, then hands over to Velopack and calls
    /// <paramref name="quit"/>. Returns false (with the reason in <see cref="State"/>) when it didn't get that far.</summary>
    public async Task<bool> ApplyAsync(Action quit)
    {
        if (_manager is not { } manager || _found is not { } found) return false;
        lock (_gate)
        {
            if (_busy) return false;
            _busy = true;
        }
        try
        {
            Set(State with { State = UpdateStateDto.Downloading, Percent = 0, Error = null });
            var last = -1;
            await manager.DownloadUpdatesAsync(found, percent =>
            {
                // Reported many times per percent; only a change is worth a broadcast.
                if (percent == Interlocked.Exchange(ref last, percent)) return;
                Set(State with { Percent = Math.Clamp(percent, 0, 100) });
            }).ConfigureAwait(false);
            Set(State with { State = UpdateStateDto.Restarting, Percent = 100 });
            _log.Info($"update {found.TargetFullRelease.Version} downloaded; quitting so it can be applied");
            manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: true, restart: true);
            quit();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("update failed", ex);
            Set(State with { State = UpdateStateDto.Available, Percent = null, Error = ex.Message });
            return false;
        }
        finally
        {
            lock (_gate) _busy = false;
        }
    }

    private void Set(UpdateDto state)
    {
        State = state;
        try
        {
            Changed?.Invoke(state);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _log.Error("update broadcast failed", ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
