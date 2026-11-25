namespace Ochre.Core.Claude;

/// <summary>
/// Finds a session's <c>{sessionId}.jsonl</c> transcript under <c>~/.claude/projects</c>. A cut-down copy of
/// Perch's <c>TranscriptLocator</c> (one config dir, no miss cache); the rest arrives with observed sessions.
/// </summary>
internal static class TranscriptLocator
{
    /// <summary>
    /// Encodes a working directory into Claude Code's project-folder name: every non-alphanumeric
    /// character becomes <c>-</c> (e.g. <c>C:\a\b.c</c> → <c>C--a-b-c</c>).
    /// </summary>
    public static string EncodeProjectDir(string cwd) =>
        string.Create(cwd.Length, cwd, static (span, s) =>
        {
            for (int i = 0; i < s.Length; i++) span[i] = char.IsAsciiLetterOrDigit(s[i]) ? s[i] : '-';
        });

    /// <summary>The transcript path, or null when it can't be found. Tries the cwd-encoded project folder first,
    /// then every project folder (the id is a UUID, so a match elsewhere is unambiguous).</summary>
    public static string? Resolve(string sessionId, string? cwd, string? configDir = null)
    {
        if (!ClaudeCli.IsSessionId(sessionId)) return null;
        var projects = string.IsNullOrWhiteSpace(configDir) ? ClaudePaths.ProjectsDir : Path.Combine(configDir, "projects");
        try
        {
            if (!string.IsNullOrEmpty(cwd))
            {
                var direct = Path.Combine(projects, EncodeProjectDir(cwd), sessionId + ".jsonl");
                if (File.Exists(direct)) return direct;
            }
            if (!Directory.Exists(projects)) return null;
            foreach (var dir in Directory.EnumerateDirectories(projects))
            {
                var candidate = Path.Combine(dir, sessionId + ".jsonl");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}
