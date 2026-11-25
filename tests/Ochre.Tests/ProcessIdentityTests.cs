using System.Diagnostics;
using Ochre.Core.Claude;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// A session file left behind by an unclean exit must not stay "alive" just because some newer process inherited
/// its pid. Liveness compares the live process's start time with the recorded one, one-sidedly: only a process that
/// started well <em>after</em> the record is a stranger. (From Perch; the scan test reads a temp folder, never the
/// real <c>~/.claude/sessions</c>.)
/// </summary>
public sealed class ProcessIdentityTests : IDisposable
{
    private static readonly DateTime Recorded = new(2026, 9, 30, 9, 0, 0);
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-sessions-").FullName;

    [Theory]
    [InlineData(-3)]        // Claude writes startedAt a beat after the process starts
    [InlineData(0)]
    [InlineData(90)]        // within the tolerance
    [InlineData(-6 * 3600)] // older than the record: it holds the pid, so it wrote the record
    public void Same_process(int processStartOffsetSeconds) =>
        Assert.False(ProcessIdentity.IsRecycled(Recorded.AddSeconds(processStartOffsetSeconds), Recorded));

    [Theory]
    [InlineData(5 * 60)]
    [InlineData(3 * 3600)]
    public void Recycled_pid(int processStartOffsetSeconds) =>
        Assert.True(ProcessIdentity.IsRecycled(Recorded.AddSeconds(processStartOffsetSeconds), Recorded));

    [Fact]
    public void The_probe_checks_the_start_time_of_a_live_process()
    {
        DateTime started;
        using (var self = Process.GetCurrentProcess()) started = self.StartTime;
        int pid = Environment.ProcessId;

        Assert.True(LiveClaudeSessions.IsAlive(pid, null));
        Assert.True(LiveClaudeSessions.IsAlive(pid, started.AddSeconds(2)));
        Assert.True(LiveClaudeSessions.IsAlive(pid, started.AddHours(5)));      // record newer than the process: same process
        Assert.False(LiveClaudeSessions.IsAlive(pid, started.AddHours(-1)));    // process newer than the record: recycled
        Assert.False(LiveClaudeSessions.IsAlive(int.MaxValue, started));        // no such process
    }

    private void WriteSession(string sessionId, DateTime startedAt)
    {
        var ms = new DateTimeOffset(startedAt).ToUnixTimeMilliseconds();
        File.WriteAllText(Path.Combine(_dir, $"{Environment.ProcessId}.json"), $$"""
            { "pid": {{Environment.ProcessId}}, "sessionId": "{{sessionId}}", "startedAt": {{ms}},
              "status": "idle", "cwd": "C:\\fixtures\\proj", "entrypoint": "cli" }
            """);
    }

    [Fact]
    public void A_session_file_whose_pid_was_recycled_is_not_live()
    {
        DateTime started;
        using (var self = Process.GetCurrentProcess()) started = self.StartTime;

        WriteSession("recycled", started.AddHours(-1));
        Assert.Null(LiveClaudeSessions.Find("recycled", _dir));

        WriteSession("same", started.AddSeconds(2));
        var live = LiveClaudeSessions.Find("same", _dir);
        Assert.NotNull(live);
        Assert.Equal(@"C:\fixtures\proj", live!.Cwd);
        Assert.Equal("cli", live.Entrypoint);
    }

    [Fact]
    public void Junk_and_half_written_files_are_skipped()
    {
        File.WriteAllText(Path.Combine(_dir, "1.json"), "{ \"pid\": 1, \"sessio");
        File.WriteAllText(Path.Combine(_dir, "2.json"), "[]");
        Assert.Empty(LiveClaudeSessions.Read(_dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
