namespace Ochre.Core.Claude;

/// <summary>
/// Where Claude Code keeps its files for the account Ochre runs under: <c>CLAUDE_CONFIG_DIR</c> when set, else
/// <c>~/.claude</c>. Several config dirs (accounts) come later; until then a session's own <c>configDir</c> is
/// passed explicitly where it matters. Nothing here touches the disk.
/// </summary>
internal static class ClaudePaths
{
    /// <summary>Case-insensitive except on Linux, matching the file system.</summary>
    public static readonly StringComparer PathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>True when <c>CLAUDE_CONFIG_DIR</c> names the config dir, which moves <c>.claude.json</c> inside it.</summary>
    public static bool IsPinned => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));

    public static string ClaudeDir =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { } pinned && !string.IsNullOrWhiteSpace(pinned)
            ? pinned.Trim()
            : Path.Combine(Home, ".claude");

    /// <summary><c>~/.claude/sessions</c>: live session sidecars.</summary>
    public static string SessionsDir => Path.Combine(ClaudeDir, "sessions");

    /// <summary><c>~/.claude/projects</c>: per-project transcript folders holding <c>{sessionId}.jsonl</c>.</summary>
    public static string ProjectsDir => Path.Combine(ClaudeDir, "projects");

    /// <summary><c>~/.claude/image-cache</c>: pasted images per session, used when reading a resumed transcript.</summary>
    public static string ImageCacheDir => Path.Combine(ClaudeDir, "image-cache");
}
