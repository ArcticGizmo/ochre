namespace Ochre.Core.Claude.Control;

/// <summary>
/// Recognises slash commands in composer text. Perch's curated palette catalogue isn't ported yet (the composer
/// palette is deferred); the conversation model only needs to know when a prompt is a command, and which.
/// </summary>
internal static class SlashCommandCatalog
{
    /// <summary>Whether composer text should be treated as a slash command: a leading <c>/</c> followed by a
    /// letter (so a lone <c>/</c>, or a path/regex the user is pasting, isn't mistaken for one).</summary>
    public static bool LooksLikeCommand(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.TrimStart();
        return t.Length >= 2 && t[0] == '/' && char.IsLetter(t[1]);
    }

    /// <summary>The command name from composer text (the token after <c>/</c>, lower-cased), or null when the
    /// text isn't a command. E.g. <c>"/compact keep tests"</c> → <c>"compact"</c>.</summary>
    public static string? CommandName(string? text)
    {
        if (!LooksLikeCommand(text)) return null;
        var t = text!.TrimStart()[1..];
        int end = 0;
        while (end < t.Length && !char.IsWhiteSpace(t[end])) end++;
        return t[..end].ToLowerInvariant();
    }
}
