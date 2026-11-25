using Ochre.Core.Claude;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Covers <see cref="JiraLink"/>, the pure branch-name to Jira key resolver behind binding suggestions. All offline
/// string work: key extraction, the project-key filter, sub-domain normalisation, and the branch-leaf parse.
/// </summary>
public class JiraTicketTests
{
    [Theory]
    [InlineData("SFTY-1234-add-audit-log", "SFTY-1234")]
    [InlineData("feature/SFTY-1234-add-thing", "SFTY-1234")]
    [InlineData("SFTY-1234", "SFTY-1234")]
    [InlineData("bugfix/PROJ-7", "PROJ-7")]
    public void Resolve_ExtractsKeyAndBuildsUrl(string branch, string expectedKey)
    {
        var ticket = JiraLink.Resolve(branch, "acme", null);

        Assert.NotNull(ticket);
        Assert.Equal(expectedKey, ticket!.Value.Key);
        Assert.Equal($"https://acme.atlassian.net/browse/{expectedKey}", ticket.Value.Url);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("develop")]
    [InlineData("sfty-1234-lowercase-no-match")]
    [InlineData("release/v1.2.3")]
    [InlineData("A-1")]
    public void Resolve_ReturnsNullWhenNoKey(string branch)
    {
        Assert.Null(JiraLink.Resolve(branch, "acme", null));
        Assert.Empty(JiraLink.Keys(branch));
    }

    [Fact]
    public void Resolve_TakesFirstKeyWhenBranchHasSeveral()
    {
        var ticket = JiraLink.Resolve("SFTY-1-then-PROJ-2", "acme", null);
        Assert.Equal("SFTY-1", ticket!.Value.Key);
    }

    [Fact]
    public void Keys_ReturnsEveryKeyInOrderOnce()
    {
        Assert.Equal(["SFTY-1", "PROJ-2"], JiraLink.Keys("SFTY-1-then-PROJ-2-and-SFTY-1"));
        Assert.Empty(JiraLink.Keys(null));
    }

    [Fact]
    public void Resolve_DoesNotMatchKeyGluedToAnotherToken()
    {
        Assert.Null(JiraLink.Resolve("xSFTY-1234", "acme", null));
    }

    [Fact]
    public void Resolve_ProjectFilterSuppressesUnlistedKeys()
    {
        var ticket = JiraLink.Resolve("PROJ-9-and-SFTY-1", "acme", "SFTY");
        Assert.Equal("SFTY-1", ticket!.Value.Key);
    }

    [Fact]
    public void Resolve_ProjectFilterWithNoMatchReturnsNull()
    {
        Assert.Null(JiraLink.Resolve("PROJ-9-only", "acme", "SFTY"));
    }

    [Theory]
    [InlineData("SFTY, PROJ")]
    [InlineData("proj sfty")]
    [InlineData("PROJ;SFTY")]
    public void Resolve_ProjectFilterAcceptsListedKey(string filter)
    {
        var ticket = JiraLink.Resolve("SFTY-42-thing", "acme", filter);
        Assert.Equal("SFTY-42", ticket!.Value.Key);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("acme.atlassian.net")]
    [InlineData("https://acme.atlassian.net")]
    [InlineData("https://acme.atlassian.net/")]
    [InlineData("  acme.atlassian.net/jira/software  ")]
    [InlineData("ACME.ATLASSIAN.NET")]
    public void NormalizeSubdomain_ReducesToSlug(string raw)
    {
        var slug = JiraLink.NormalizeSubdomain(raw);
        Assert.False(string.IsNullOrEmpty(slug));
        Assert.DoesNotContain("/", slug);
        Assert.DoesNotContain("atlassian.net", slug, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".atlassian.net")]
    public void NormalizeSubdomain_ReturnsNullForEmpty(string? raw)
    {
        Assert.Null(JiraLink.NormalizeSubdomain(raw));
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNoSiteConfigured()
    {
        Assert.Null(JiraLink.Resolve("SFTY-1234", null, null));
        Assert.Null(JiraLink.Resolve("SFTY-1234", "   ", null));
    }

    [Fact]
    public void Resolve_AcceptsFullHostAsSubdomain()
    {
        var ticket = JiraLink.Resolve("SFTY-1234", "https://acme.atlassian.net/", null);
        Assert.Equal("https://acme.atlassian.net/browse/SFTY-1234", ticket!.Value.Url);
    }

    [Theory]
    [InlineData("ref: refs/heads/SFTY-1234-add-thing", "SFTY-1234-add-thing")]
    [InlineData("ref: refs/heads/feature/PROJ-7", "feature/PROJ-7")]
    [InlineData("  ref: refs/heads/main  ", "main")]
    public void BranchFromHeadRef_ParsesLeaf(string headRef, string expected)
    {
        Assert.Equal(expected, JiraLink.BranchFromHeadRef(headRef));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9d3a1c0f8b2e4a6d7c5f0a1b2c3d4e5f60718293")]
    public void BranchFromHeadRef_ReturnsNullForDetachedOrEmpty(string? headRef)
    {
        Assert.Null(JiraLink.BranchFromHeadRef(headRef));
    }

    [Fact]
    public void EndToEnd_HeadRefToTicket()
    {
        var branch = JiraLink.BranchFromHeadRef("ref: refs/heads/SFTY-9001-warp-speed");
        var ticket = JiraLink.Resolve(branch, "acme.atlassian.net", "SFTY");

        Assert.Equal("SFTY-9001", ticket!.Value.Key);
        Assert.Equal("https://acme.atlassian.net/browse/SFTY-9001", ticket.Value.Url);
    }
}
