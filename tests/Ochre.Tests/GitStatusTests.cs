using Ochre.Core.Git;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The parser behind every branch, change count and ahead/behind Ochre shows, against canned
/// <c>git status --porcelain=v2 --branch</c> output exactly as git emits it.
/// </summary>
public class GitStatusTests
{
    [Fact]
    public void Status_ReadsBranchUpstreamAndAheadBehind()
    {
        var s = GitStatusReader.ParseStatusV2(
            "# branch.oid abc123\n" +
            "# branch.head main\n" +
            "# branch.upstream origin/main\n" +
            "# branch.ab +2 -3\n");

        Assert.Equal("main", s.Branch);
        Assert.Equal("origin/main", s.Upstream);
        Assert.Equal(2, s.Ahead);
        Assert.Equal(3, s.Behind);
        Assert.True(s.IsClean);
    }

    [Fact]
    public void Status_DetachedHeadIsNullBranch()
    {
        var s = GitStatusReader.ParseStatusV2("# branch.head (detached)\n");
        Assert.Null(s.Branch);
    }

    [Fact]
    public void Status_OrdinaryChangesMapStagedAndUnstaged()
    {
        // "1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>"
        var s = GitStatusReader.ParseStatusV2(
            "# branch.head main\n" +
            "1 M. N... 100644 100644 100644 1111111 2222222 src/Staged.cs\n" +
            "1 .M N... 100644 100644 100644 3333333 4444444 src/Worktree.cs\n" +
            "1 A. N... 000000 100644 100644 0000000 5555555 src/New Added.cs\n");

        Assert.Equal(3, s.Changes.Count);
        Assert.False(s.IsClean);

        var staged = s.Changes[0];
        Assert.Equal("src/Staged.cs", staged.Path);
        Assert.Equal(GitChangeKind.Modified, staged.Staged);
        Assert.Equal(GitChangeKind.None, staged.Unstaged);

        var worktree = s.Changes[1];
        Assert.Equal(GitChangeKind.None, worktree.Staged);
        Assert.Equal(GitChangeKind.Modified, worktree.Unstaged);

        // A path with a space survives the field-limited split.
        var added = s.Changes[2];
        Assert.Equal("src/New Added.cs", added.Path);
        Assert.Equal(GitChangeKind.Added, added.Staged);
    }

    [Fact]
    public void Status_RenameCarriesOriginalPath()
    {
        // "2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <score> <newPath>\t<origPath>"
        var s = GitStatusReader.ParseStatusV2(
            "# branch.head main\n" +
            "2 R. N... 100644 100644 100644 1111111 2222222 R100 src/New.cs\tsrc/Old.cs\n");

        var c = Assert.Single(s.Changes);
        Assert.Equal("src/New.cs", c.Path);
        Assert.Equal("src/Old.cs", c.OrigPath);
        Assert.Equal(GitChangeKind.Renamed, c.Staged);
    }

    [Fact]
    public void Status_UntrackedAndUnmergedAndIgnored()
    {
        var s = GitStatusReader.ParseStatusV2(
            "# branch.head main\n" +
            "? build/output.txt\n" +
            "u UU N... 100644 100644 100644 100644 111 222 333 src/Conflict.cs\n" +
            "! .idea/workspace.xml\n");

        // The ignored entry is skipped.
        Assert.Equal(2, s.Changes.Count);

        var untracked = s.Changes[0];
        Assert.Equal("build/output.txt", untracked.Path);
        Assert.True(untracked.Untracked);

        var conflict = s.Changes[1];
        Assert.Equal("src/Conflict.cs", conflict.Path);
        Assert.Equal(GitChangeKind.Unmerged, conflict.Staged);
        Assert.Equal(GitChangeKind.Unmerged, conflict.Unstaged);
    }

    [Fact]
    public void Status_ToleratesCrlfBlankAndMalformedLines()
    {
        var s = GitStatusReader.ParseStatusV2(
            "# branch.head main\r\n" +
            "\r\n" +
            "1 M.\r\n" +
            "garbage\n" +
            "1 .M N... 100644 100644 100644 3333333 4444444 ok.cs\r\n");

        Assert.Equal("main", s.Branch);
        var c = Assert.Single(s.Changes);
        Assert.Equal("ok.cs", c.Path);
    }

    [Fact]
    public void Status_EmptyOutputIsCleanTree()
    {
        var s = GitStatusReader.ParseStatusV2("");
        Assert.True(s.IsClean);
        Assert.Null(s.Branch);
    }
}
