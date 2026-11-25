using Ochre.Core.Platform;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Every link Ochre opens passes through <see cref="OpenTargets.WebUrl"/>, so a link pointing at a
/// <c>file:</c>/UNC/<c>search-ms:</c> target can't execute via the shell, and a <c>--switch</c> can't reach the
/// browser exe.
/// </summary>
public class OpenTargetsTests
{
    [Theory]
    [InlineData("file:///C:/x.exe")]
    [InlineData("file://attacker/share/x.exe")]
    [InlineData(@"\\h\s\x")]
    [InlineData(@"\\h\s\x.exe")]
    [InlineData("//h/s/x")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("search-ms:query=x&crumb=location:\\\\attacker\\s")]
    [InlineData("ms-settings:privacy")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("--gpu-launcher=calc.exe")]
    [InlineData("-new-window")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vscode://file/C:/x")]
    [InlineData("https://")]
    [InlineData("docs/plan.md")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void WebUrl_rejects_anything_but_http_and_mailto(string? url) =>
        Assert.Null(OpenTargets.WebUrl(url));

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://github.com/o/r/pull/1/checks?check_run_id=9#step:3", "https://github.com/o/r/pull/1/checks?check_run_id=9#step:3")]
    [InlineData("HTTP://Example.COM/a", "http://example.com/a")]
    [InlineData("  https://example.com/x  ", "https://example.com/x")]
    [InlineData("mailto:support@example.com", "mailto:support@example.com")]
    public void WebUrl_accepts_and_normalises_web_links(string url, string expected) =>
        Assert.Equal(expected, OpenTargets.WebUrl(url));

    [Fact]
    public void WebUrl_result_is_always_scheme_led()
    {
        // Whitespace and control characters are escaped, so nothing after the URL can become a separate argument
        // and nothing can precede the scheme.
        var safe = OpenTargets.WebUrl("https://example.com/a b\n--gpu-launcher=calc");
        Assert.NotNull(safe);
        Assert.StartsWith("https://example.com/", safe);
        Assert.DoesNotContain(' ', safe);
        Assert.DoesNotContain('\n', safe);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:a@example.com", false)]
    public void IsHttp_separates_browser_links_from_mailto(string url, bool expected) =>
        Assert.Equal(expected, OpenTargets.IsHttp(url));
}
