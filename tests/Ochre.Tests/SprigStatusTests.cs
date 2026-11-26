using Ochre.Core;
using Ochre.Core.Modules;
using Ochre.Core.Tasks;
using Ochre.Modules.Sprig;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Reading <c>sprig watch --json</c> and the outcome of a sprig command. The stream is another tool's contract,
/// possibly from a newer sprig, so the interesting cases are the tolerant ones: unknown types and fields, partial
/// lines, and lines about workspaces we haven't seen.
/// </summary>
public class SprigStatusTests
{
    // Shaped like a live sprig 0.9.10 snapshot: one slot running two of its three apps, one paused, one free.
    private const string Snapshot = """
        {"type":"snapshot","schema":1,"infra":"not-counted","host":{"running":true,"pid":4120,"version":"0.9.10"},"workspaces":[{"key":"shop/1","orchestration":"shop","name":"1","label":"feat-cart","branch":"feat-cart","state":"running","paused":false,"folder":"C:\\src\\shop--1","dirs":["C:\\src\\shop--1"],"madeForAgent":"5f0c-session","apps":[{"name":"shop/api","state":"ready","port":5123,"since":"2026-11-18T10:02:11Z","restarts":0,"logFile":"C:\\logs\\api.log"},{"name":"shop/web","state":"building","doing":"installing packages","since":"2026-11-18T10:02:40Z","restarts":0},{"name":"shop/worker","state":"off"}]},{"key":"shop/2","orchestration":"shop","name":"2","label":"fix-login","branch":"fix-login","state":"paused","paused":true,"dirs":["C:\\src\\shop--2"],"apps":[{"name":"shop/api","state":"off"}]},{"key":"shop/3","orchestration":"shop","name":"3","label":"free","state":"free","paused":false,"dirs":[],"apps":[]}]}
        """;

    private static SprigStatusFold Folded()
    {
        var fold = new SprigStatusFold();
        Assert.True(fold.Apply(Snapshot));
        return fold;
    }

    [Fact]
    public void Snapshot_ReadsHostWorkspacesAndApps()
    {
        var s = Folded().Current;

        Assert.True(s.Seen);
        Assert.True(s.HostRunning);
        Assert.Equal("0.9.10", s.HostVersion);
        Assert.Equal(["shop/1", "shop/2", "shop/3"], s.Workspaces.Select(w => w.Key));

        var one = s.Find("shop/1")!;
        Assert.Equal(SprigWorkspaceState.Running, one.State);
        Assert.Equal("feat-cart", one.Label);
        Assert.Equal("5f0c-session", one.MadeForAgent);
        Assert.Equal([@"C:\src\shop--1"], one.Dirs);
        Assert.Equal(1, one.ReadyCount);
        Assert.Equal(2, one.ActiveCount);

        var api = one.Apps[0];
        Assert.Equal(5123, api.Port);
        Assert.Equal("api", api.ShortName);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 2, 11, TimeSpan.Zero), api.Since);
        Assert.Equal("installing packages", one.Apps[1].Doing);
        Assert.True(one.Apps[2].IsOff);

        Assert.True(s.Find("shop/2")!.Paused);
        Assert.True(s.Find("shop/3")!.IsFree);
    }

    [Fact]
    public void Shown_RunningWithAppsStillComingUp_ReadsAsStarting()
    {
        var fold = Folded();
        // Sprig says running; web is still building.
        var one = fold.Current.Find("shop/1")!;
        Assert.Equal(SprigWorkspaceState.Running, one.State);
        Assert.Equal(SprigWorkspaceState.Starting, one.Shown);

        // Worker is off by choice.
        fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/web","state":"ready","port":5124}""");
        Assert.Equal(SprigWorkspaceState.Running, fold.Current.Find("shop/1")!.Shown);

        // Going down isn't starting.
        fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/web","state":"stopping"}""");
        Assert.Equal(SprigWorkspaceState.Running, fold.Current.Find("shop/1")!.Shown);

        // A crash loop isn't a start: it's trouble (red), as sprig's own readiness counts it.
        fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/web","state":"restarting","restarts":4}""");
        var looping = fold.Current.Find("shop/1")!;
        Assert.Equal(SprigWorkspaceState.Running, looping.Shown);
        Assert.True(looping.InTrouble);
        Assert.Equal(["shop/web"], looping.Down);
    }

    [Fact]
    public void Activity_StartingWhileAppsComeUp_StoppingWhileAnyGoesDown()
    {
        var fold = Folded();
        // Web is building; shop/2 is paused with nothing moving.
        Assert.Equal(SprigActivity.Starting, fold.Current.Find("shop/1")!.Activity);
        Assert.Equal(SprigActivity.None, fold.Current.Find("shop/2")!.Activity);

        fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/web","state":"ready","port":5124}""");
        Assert.Equal(SprigActivity.None, fold.Current.Find("shop/1")!.Activity);

        // Sprig has no "stopping" workspace state: a stop shows as the apps going down, and outranks a start.
        fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/api","state":"stopping"}""");
        fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/web","state":"building"}""");
        Assert.Equal(SprigActivity.Stopping, fold.Current.Find("shop/1")!.Activity);
    }

    [Fact]
    public void ActivityOf_StopAndPauseGoDown_EverythingElseComesUp()
    {
        Assert.Equal(SprigActivity.Starting, SprigActions.ActivityOf(SprigAction.Start));
        Assert.Equal(SprigActivity.Starting, SprigActions.ActivityOf(SprigAction.Restart));
        Assert.Equal(SprigActivity.Starting, SprigActions.ActivityOf(SprigAction.Resume));
        Assert.Equal(SprigActivity.Stopping, SprigActions.ActivityOf(SprigAction.Stop));
        Assert.Equal(SprigActivity.Stopping, SprigActions.ActivityOf(SprigAction.Pause));
    }

    // A workspace line from a sprig that sends readiness (its starting-state change).
    private const string WithReadiness = """
        {"type":"workspace","key":"shop/1","orchestration":"shop","name":"1","label":"feat-cart","state":"running","paused":false,"dirs":[],"readiness":{"health":"HEALTH","ready":1,"started":3,"total":4,"down":DOWN}}
        """;

    private static SprigWorkspace Readied(string health, string down = "[]")
    {
        var fold = Folded();
        Assert.True(fold.Apply(WithReadiness.Replace("HEALTH", health, StringComparison.Ordinal).Replace("DOWN", down, StringComparison.Ordinal)));
        return fold.Current.Find("shop/1")!;
    }

    [Fact]
    public void Readiness_IsRead_AndWinsOverTheAppList()
    {
        var w = Readied("starting");
        var r = Assert.IsType<SprigReadiness>(w.Readiness);
        Assert.Equal(("starting", 1, 3, 4), (r.Health, r.Ready, r.Started, r.Total));
        Assert.Empty(r.Down);
        Assert.Equal(SprigWorkspaceState.Starting, w.Shown);
        // Sprig's counts (jobs and apps stopped on purpose left out).
        Assert.Equal((1, 3), w.ReadyOf);

        // "up" from sprig wins even though the app lines we hold still have web building.
        var up = Readied("up");
        Assert.Equal(SprigWorkspaceState.Running, up.Shown);
        Assert.True(up.AllUp);
        Assert.False(up.InTrouble);
    }

    [Fact]
    public void Readiness_Trouble_IsRedWithWhatsDown()
    {
        var w = Readied("trouble", """["shop/api","shop/web"]""");
        Assert.True(w.InTrouble);
        Assert.Equal(["shop/api", "shop/web"], w.Down);
        Assert.Equal(SprigWorkspaceState.Running, w.Shown);
    }

    [Fact]
    public void Readiness_OnlyMattersWhileRunning()
    {
        var fold = Folded();
        fold.Apply(WithReadiness.Replace("\"running\"", "\"paused\"", StringComparison.Ordinal).Replace("HEALTH", "trouble", StringComparison.Ordinal).Replace("DOWN", "[]", StringComparison.Ordinal));
        var w = fold.Current.Find("shop/1")!;
        Assert.Equal(SprigWorkspaceState.Paused, w.Shown);
        Assert.False(w.InTrouble);
    }

    // A snapshot as sprig 0.9.11 sends it (captured live, trimmed): readiness on every workspace, "none" while
    // nothing runs; and a stopped workspace whose apps all exited, which reports trouble but is shown stopped.
    private const string Sprig0911 = """
        {"type":"snapshot","schema":1,"infra":"not-counted","host":{"running":true,"pid":9,"version":"0.9.11"},"workspaces":[{"key":"Safety-Virtual/1","orchestration":"Safety-Virtual","name":"1","label":"NO-TICKET-injuries-v2-core","branch":"NO-TICKET-injuries-v2-core","state":"paused","paused":true,"folder":"C:\\git\\Safety-Virtual--1","dirs":["C:\\git\\Safety-Virtual--1"],"readiness":{"health":"none","ready":0,"started":0,"total":8,"down":[]},"apps":[{"name":"safety/client","state":"off"}]},{"key":"Safety-Virtual/3","orchestration":"Safety-Virtual","name":"3","label":"migration","state":"stopped","paused":false,"dirs":[],"readiness":{"health":"trouble","ready":0,"started":0,"total":8,"down":["safety/client"]},"apps":[{"name":"safety/client","state":"exited","exit":1}]},{"key":"Safety-Virtual/2","orchestration":"Safety-Virtual","name":"2","label":"onboarding","state":"running","paused":false,"dirs":[],"readiness":{"health":"starting","ready":0,"started":0,"total":8,"down":[]},"apps":[{"name":"safety/migrate","state":"running"}]}]}
        """;

    [Fact]
    public void Readiness_Sprig0911Snapshot_ReadsAsSprigShowsIt()
    {
        var fold = new SprigStatusFold();
        Assert.True(fold.Apply(Sprig0911));

        var paused = fold.Current.Find("Safety-Virtual/1")!;
        Assert.Equal(SprigReadiness.None, paused.Readiness!.Health);
        Assert.Equal(8, paused.Readiness.Total);
        Assert.Equal(SprigWorkspaceState.Paused, paused.Shown);

        // Trouble, but nothing is going: shown stopped.
        var stopped = fold.Current.Find("Safety-Virtual/3")!;
        Assert.Equal(SprigWorkspaceState.Stopped, stopped.Shown);
        Assert.False(stopped.InTrouble);

        // Only a job running so far.
        var jobs = fold.Current.Find("Safety-Virtual/2")!;
        Assert.Equal(SprigWorkspaceState.Starting, jobs.Shown);
        Assert.Equal((0, 0), jobs.ReadyOf);
    }

    [Fact]
    public void Readiness_Absent_FallsBackToTheApps()
    {
        // The 0.9.10-shaped snapshot has no readiness.
        var w = Folded().Current.Find("shop/1")!;
        Assert.Null(w.Readiness);
        Assert.Equal((1, 2), w.ReadyOf);
        Assert.False(w.AllUp);
    }

    [Fact]
    public void AppLine_ReplacesThatAppOnly()
    {
        var fold = Folded();

        Assert.True(fold.Apply("""{"type":"app","workspace":"shop/1","name":"shop/web","state":"ready","port":5124,"restarts":0}"""));

        var one = fold.Current.Find("shop/1")!;
        Assert.Equal(2, one.ReadyCount);
        Assert.Equal(5124, one.Apps[1].Port);
        // An app line carries every field, so what it leaves out is gone.
        Assert.Null(one.Apps[1].Doing);
        Assert.Equal("ready", one.Apps[0].State);
    }

    [Fact]
    public void AppLine_ForAnAppNotListedYet_IsAdded()
    {
        var fold = Folded();

        Assert.True(fold.Apply("""{"type":"app","workspace":"shop/2","name":"shop/jobs","state":"running"}"""));

        Assert.Equal(["shop/api", "shop/jobs"], fold.Current.Find("shop/2")!.Apps.Select(a => a.Name));
    }

    [Fact]
    public void WorkspaceLine_KeepsTheAppsWeHold()
    {
        var fold = Folded();

        Assert.True(fold.Apply("""{"type":"workspace","key":"shop/1","orchestration":"shop","name":"1","label":"feat-cart","branch":"feat-cart","state":"stopped","paused":true,"dirs":["C:\\src\\shop--1"]}"""));

        var one = fold.Current.Find("shop/1")!;
        Assert.Equal(SprigWorkspaceState.Stopped, one.State);
        Assert.True(one.Paused);
        Assert.Equal(3, one.Apps.Count);
    }

    [Fact]
    public void WorkspaceLine_ForANewWorkspace_IsAddedWithNoApps()
    {
        var fold = Folded();

        Assert.True(fold.Apply("""{"type":"workspace","key":"other/1","orchestration":"other","name":"1","label":"main-ish","state":"starting","paused":false,"dirs":[]}"""));

        var added = fold.Current.Find("other/1")!;
        Assert.Equal(SprigWorkspaceState.Starting, added.State);
        Assert.Empty(added.Apps);
    }

    [Fact]
    public void Removals_DropTheWorkspaceOrApp()
    {
        var fold = Folded();

        Assert.True(fold.Apply("""{"type":"app-removed","workspace":"shop/1","name":"shop/worker"}"""));
        Assert.True(fold.Apply("""{"type":"workspace-removed","key":"shop/3"}"""));

        Assert.Equal(2, fold.Current.Find("shop/1")!.Apps.Count);
        Assert.Null(fold.Current.Find("shop/3"));
        // Already gone.
        Assert.False(fold.Apply("""{"type":"workspace-removed","key":"shop/3"}"""));
    }

    [Fact]
    public void HostLine_MarksTheHostGone_AndANewSnapshotBringsItBack()
    {
        var fold = Folded();

        Assert.True(fold.Apply("""{"type":"host","running":false}"""));
        Assert.False(fold.Current.HostRunning);
        Assert.Null(fold.Current.HostVersion);
        Assert.False(fold.Apply("""{"type":"host","running":false}"""));

        Assert.True(fold.Apply(Snapshot));
        Assert.True(fold.Current.HostRunning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("""{"type":"snapshot","workspaces":[""")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"type":"usage-spike","cpu":99}""")]
    [InlineData("""{"type":"app","workspace":"nope/9","name":"x","state":"ready"}""")]
    [InlineData("""{"type":"workspace"}""")]
    public void UnusableLines_AreSkipped_AndChangeNothing(string line)
    {
        // Cut off mid-line, a type a newer sprig added, a workspace we never saw, a workspace with no key: all skipped.
        var fold = Folded();
        var before = fold.Current;

        Assert.False(fold.Apply(line));
        Assert.Same(before, fold.Current);
    }

    [Fact]
    public void UnknownFieldsAndStates_AreTolerated()
    {
        var fold = new SprigStatusFold();

        Assert.True(fold.Apply("""{"type":"snapshot","schema":2,"newThing":{"a":1},"host":{"running":false},"workspaces":[{"key":"x/1","state":"hibernating","label":"b","apps":[{"name":"x/a","state":"warming","port":"not a number"}]}]}"""));

        var w = fold.Current.Find("x/1")!;
        Assert.Equal(SprigWorkspaceState.Unknown, w.State);
        Assert.Equal("hibernating", w.StateText);
        // Inferred from the key when the field is missing.
        Assert.Equal("x", w.Orchestration);
        Assert.Null(w.Apps[0].Port);
        Assert.False(fold.Current.HostRunning);
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        var fold = Folded();
        fold.Reset();
        Assert.Same(SprigStatus.Empty, fold.Current);
    }

    // ---- task folders ---------------------------------------------------------------------------------------------

    // Rooted for whichever OS runs the suite, since the matcher normalizes through Path.GetFullPath.
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\src" : "/src";

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    private static SprigStatus Slots()
    {
        static SprigWorkspace Ws(string slot, SprigWorkspaceState state, params string[] dirs)
            => new($"shop/{slot}", "shop", slot, $"b{slot}", $"b{slot}", state, "", false, null, null, dirs, null, []);

        return new SprigStatus(true, true, null,
        [
            Ws("main", SprigWorkspaceState.Running, P("shop")),
            Ws("1", SprigWorkspaceState.Running, P("shop--1")),
            Ws("10", SprigWorkspaceState.Stopped, P("shop--10")),
            Ws("2", SprigWorkspaceState.Free, P("shop--2")),
            Ws("3", SprigWorkspaceState.Paused, P("shop--3", "api"), P("shop--3", "web")),
        ]);
    }

    [Fact]
    public void Folder_InsideADir_OnASeparatorBoundary()
    {
        Assert.Equal("shop/1", SprigFolders.WorkspaceOf(Slots(), P("shop--1", "src", "Api"))?.Key);
        // Not shop--1.
        Assert.Equal("shop/10", SprigFolders.WorkspaceOf(Slots(), P("shop--10"))?.Key);
        Assert.Equal("shop/main", SprigFolders.WorkspaceOf(Slots(), P("shop", "docs"))?.Key);
        Assert.Equal("shop/3", SprigFolders.WorkspaceOf(Slots(), P("shop--3", "web", "src"))?.Key);
    }

    [Fact]
    public void Folder_FreeSlotsElsewhereAndBeforeTheFirstSnapshot_DontMatch()
    {
        Assert.Null(SprigFolders.WorkspaceOf(Slots(), P("shop--2", "api")));
        Assert.Null(SprigFolders.WorkspaceOf(Slots(), P("elsewhere")));
        Assert.Null(SprigFolders.WorkspaceOf(Slots(), null));
        Assert.Null(SprigFolders.WorkspaceOf(SprigStatus.Empty, P("shop--1")));
    }

    [Fact]
    public void Folder_TrailingSeparatorsAndSlashes_AreIgnored()
    {
        var withSlash = P("shop--1") + Path.DirectorySeparatorChar;
        Assert.Equal("shop/1", SprigFolders.WorkspaceOf(Slots(), withSlash)?.Key);
        Assert.Equal("shop/1", SprigFolders.WorkspaceOf(Slots(), P("shop--1").Replace('\\', '/'))?.Key);
    }

    [Fact]
    public void A_tasks_folders_group_by_the_workspace_they_are_in()
    {
        var matched = SprigModule.Matched(Slots(), [P("shop"), P("shop--3", "api"), P("shop--3", "web"), P("elsewhere")]);

        Assert.Equal(["shop/main", "shop/3"], matched.Select(m => m.Workspace.Key));
        Assert.Equal([P("shop--3", "api"), P("shop--3", "web")], matched[1].Folders);
    }

    // ---- commands -------------------------------------------------------------------------------------------------

    private static SprigWorkspace Workspace() => Folded().Current.Find("shop/1")!;

    [Fact]
    public void ActionArgs_NameTheWorkspace_AndNeverWaitForStarts()
    {
        Assert.Equal(["start", "shop", "1", "--json", "--no-wait"], SprigCli.ActionArgs(SprigAction.Start, Workspace(), null));
        Assert.Equal(["restart", "shop", "1", "shop/api", "--json", "--no-wait"], SprigCli.ActionArgs(SprigAction.Restart, Workspace(), "shop/api"));
        Assert.Equal(["stop", "shop", "1", "shop/api", "--json"], SprigCli.ActionArgs(SprigAction.Stop, Workspace(), "shop/api"));
        Assert.Equal(["pause", "shop", "1", "--json"], SprigCli.ActionArgs(SprigAction.Pause, Workspace(), null));
        Assert.Equal(["resume", "shop", "1", "--json", "--no-wait"], SprigCli.ActionArgs(SprigAction.Resume, Workspace(), null));
    }

    [Fact]
    public void ActionArgs_PauseAndResume_IgnoreAnApp()
    {
        Assert.Equal(["pause", "shop", "1", "--json"], SprigCli.ActionArgs(SprigAction.Pause, Workspace(), "shop/api"));
    }

    [Fact]
    public void Parse_ExitZero_IsOk()
    {
        var r = SprigCli.Parse(0, """{"ok":true,"apps":["shop/api"]}""", "");
        Assert.True(r.Ok);
    }

    [Fact]
    public void Parse_Failure_ReadsTheLastJsonObject()
    {
        var stdout = """
            {"event":"progress","text":"making worktrees"}
            {"ok":false,"code":"untrusted","error":"sprig.yaml isn't approved","untrusted":["C:\\src\\sprig.yaml"]}
            """;
        var r = SprigCli.Parse(1, stdout, "");

        Assert.False(r.Ok);
        Assert.Equal("untrusted", r.Code);
        Assert.Equal("sprig.yaml isn't approved", r.Error);
        Assert.True(r.NeedsSprig);
        Assert.Equal("sprig.yaml isn't approved (untrusted)", r.Message);
    }

    // Sprig 0.9.15 pretty-prints its answers across lines (captured live).
    [Fact]
    public void Parse_PrettyPrintedFailure_IsRead()
    {
        var stdout = "{\n  \"ok\": false,\n  \"code\": \"error\",\n  \"error\": \"these worktrees have changes that aren\\u0027t committed; commit or stash them first:\\n  C:\\\\src\\\\web--1\",\n  \"problems\": [],\n  \"asks\": [],\n  \"untrusted\": [],\n  \"heldBy\": null\n}\n";
        var r = SprigCli.Parse(1, stdout, "");

        Assert.Equal("error", r.Code);
        Assert.StartsWith("these worktrees have changes that aren't committed", r.Error, StringComparison.Ordinal);
        Assert.False(r.NeedsSprig);
    }

    [Fact]
    public void Parse_FailureWithoutJson_FallsBackToStderr()
    {
        var r = SprigCli.Parse(1, "", "error: shop has no workspace named '9'\n");

        Assert.False(r.Ok);
        Assert.Equal("error", r.Code);
        Assert.Equal("shop has no workspace named '9'", r.Error);
        Assert.False(r.NeedsSprig);
    }

    [Fact]
    public void Parse_DockerUnavailable_IsItsOwnCode()
    {
        var r = SprigCli.Parse(1, """{"ok":false,"code":"docker-unavailable","error":"Docker isn't running"}""", "");
        Assert.Equal("docker-unavailable", r.Code);
        Assert.False(r.NeedsSprig);
    }

    // `sprig checkout -b <branch> --json` from sprig 0.9.15, trimmed: where the task's repo landed in the slot.
    [Fact]
    public void Checkout_answer_names_the_slot_and_the_repos_folder_in_it()
    {
        var repo = P("web");
        var stdout = $$"""
            {
              "ok": true,
              "workspace": {
                "Orchestration": "web",
                "Name": "1",
                "Branch": "t-0099-demo",
                "Repos": [
                  {
                    "RepoPath": "web",
                    "Source": "{{repo.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                    "Dir": "{{P("web--1").Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                    "Worktree": true,
                    "BranchCreated": true,
                    "From": "main"
                  }
                ],
                "Folder": "{{P("web--1").Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                "Key": "web/1"
              },
              "secretsUnmasked": false
            }
            """;

        var checkout = SprigCli.ParseCheckout(stdout, repo + Path.DirectorySeparatorChar);

        Assert.Equal(new SprigCheckout("web/1", P("web--1"), "t-0099-demo", true), checkout);
        Assert.Null(SprigCli.ParseCheckout("""{"ok":true}""", repo));
    }

    // ---- giving a slot back ---------------------------------------------------------------------------------------

    // Before sprig's first snapshot every slot looks given back; removing then would take the entry off the task and
    // leave the slot taken, with nothing said.
    [Fact]
    public async Task A_slot_is_never_given_back_before_sprig_has_reported()
    {
        using var log = new AppLog(Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N")));
        using var watch = new SprigWatch(log);
        var provider = new SprigWorktreeProvider(watch);
        var worktree = new TaskWorktree(P("web--1"), P("web"), "sprig", "t-1", null, "web/1");

        await Assert.ThrowsAsync<WorktreeException>(() => provider.CheckAsync(worktree, CancellationToken.None));
        await Assert.ThrowsAsync<WorktreeException>(() => provider.RemoveAsync(worktree, false, false, CancellationToken.None));
    }

    [Fact]
    public void A_logs_tail_drops_a_cut_first_line_and_keeps_the_last_lines()
    {
        var path = Path.Combine(Path.GetTempPath(), "ochre-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"line {i}")) + "\n");
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                // Still being written by sprig's host: read anyway.
                var (text, truncated) = LogTail.Read(path, maxLines: 5)!.Value;
                Assert.Equal("line 996\nline 997\nline 998\nline 999\nline 1000", text);
                Assert.True(truncated);
            }

            var (bytes, cut) = LogTail.Read(path, maxLines: 1000, maxBytes: 30)!.Value;
            // 30 bytes from the end starts mid-line; that part line is dropped, so every line shown is whole.
            Assert.True(cut);
            Assert.All(bytes.Split('\n'), l => Assert.StartsWith("line ", l, StringComparison.Ordinal));
            Assert.EndsWith("line 1000", bytes, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Null(LogTail.Read(path));
    }
}
