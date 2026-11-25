namespace Ochre.Core.Platform;

/// <summary>
/// Decides what Ochre may hand to the OS to "open". Links arrive from places Ochre didn't write (agent output,
/// Jira, markdown), and the OS shell will happily execute most of what it's given: a <c>file:</c> or UNC target
/// runs an executable, <c>search-ms:</c> / <c>ms-*:</c> launch protocol handlers, and a string beginning with
/// <c>-</c> passed to a browser exe becomes a command-line switch. Pure; never throws.
/// </summary>
public static class OpenTargets
{
    /// <summary>
    /// The normalised form of <paramref name="url"/> when it is safe to open in a browser or mail client (an
    /// absolute <c>http</c>/<c>https</c> URL with a host, or a <c>mailto:</c>), else null. The result always
    /// begins with its scheme, so it can never be read as a command-line switch.
    /// </summary>
    public static string? WebUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        // Uri.TryCreate also accepts a Windows path ("C:\x") or UNC ("\\h\s") as absolute, as file: URIs, which
        // the scheme check below rejects.
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        return uri.Scheme switch
        {
            "http" or "https" when uri.Host.Length > 0 => uri.AbsoluteUri,
            "mailto" => uri.AbsoluteUri,
            _ => null,
        };
    }

    /// <summary>True for an http(s) URL, the only kind worth handing to a browser executable.</summary>
    public static bool IsHttp(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
