using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Ochre.Core.Claude;

/// <summary>One running Claude Code process, as its <c>sessions/{pid}.json</c> describes it.</summary>
/// <param name="Entrypoint">How it was started: <c>cli</c>, <c>claude-desktop</c>, <c>sdk-ts</c>, <c>sdk-py</c>.</param>
internal sealed record LiveClaudeSession(int Pid, string SessionId, string Cwd, string Status, string? Entrypoint, DateTime? StartedAt);

/// <summary>
/// Reads Claude Code's live session files. Taken from Perch's <c>SessionMonitor.ReadSession</c> and
/// <c>SystemProcessProbe</c> without the rest of the monitor. A file outlives an unclean exit, so a session only
/// counts while its pid is alive and wasn't recycled (<see cref="ProcessIdentity"/>).
/// </summary>
internal static class LiveClaudeSessions
{
    /// <summary>Every live session in <paramref name="sessionsDir"/> (default: <see cref="ClaudePaths.SessionsDir"/>).
    /// Never throws; unreadable files are skipped.</summary>
    public static IReadOnlyList<LiveClaudeSession> Read(string? sessionsDir = null)
    {
        var dir = sessionsDir ?? ClaudePaths.SessionsDir;
        var found = new List<LiveClaudeSession>();
        try
        {
            if (!Directory.Exists(dir)) return found;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                if (ReadFile(file) is { } s && IsAlive(s.Pid, s.StartedAt)) found.Add(s);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return found;
    }

    /// <summary>The live process running <paramref name="sessionId"/>, if any.</summary>
    public static LiveClaudeSession? Find(string sessionId, string? sessionsDir = null) =>
        Read(sessionsDir).FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));

    internal static LiveClaudeSession? ReadFile(string path)
    {
        try
        {
            string json;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs))
                json = reader.ReadToEnd();
            if (JsonNode.Parse(json) is not JsonObject node) return null;
            var pid = (int)TranscriptJson.AsLong(node["pid"]);
            if (pid <= 0 && !int.TryParse(Path.GetFileNameWithoutExtension(path), out pid)) return null;
            var sessionId = TranscriptJson.AsString(node["sessionId"]) ?? "";
            if (sessionId.Length == 0) return null;
            var startedMs = TranscriptJson.AsLong(node["startedAt"]);
            return new LiveClaudeSession(
                pid,
                sessionId,
                TranscriptJson.AsString(node["cwd"]) ?? "",
                TranscriptJson.AsString(node["status"]) ?? "idle",
                TranscriptJson.AsString(node["entrypoint"]),
                startedMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(startedMs).LocalDateTime : null);
        }
        catch
        {
            return null;   // a file mid-write, or not a session file
        }
    }

    /// <summary>True while the OS has a non-exited process for <paramref name="pid"/> that isn't a newer process
    /// which inherited it. <paramref name="startedAt"/> is the recorded start (local), null to skip that check.</summary>
    public static bool IsAlive(int pid, DateTime? startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            if (startedAt is not { } recorded) return true;
            DateTime processStart;
            try { processStart = process.StartTime; }
            catch { return true; }   // can't read its start time: keep the pid-only answer rather than hide a session
            return !ProcessIdentity.IsRecycled(processStart, recorded);
        }
        catch
        {
            return false;
        }
    }
}
