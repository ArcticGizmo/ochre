using Ochre.Core.Git;
using Ochre.Modules.Git;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Inferring where a user keeps a repo's worktrees from the ones they already have (or an ignore-file hint), and
/// rendering a new worktree's path the same way. Pure.
/// </summary>
public class WorktreeLayoutTests
{
    private static readonly string Src = OperatingSystem.IsWindows() ? @"C:\src" : "/src";
    private static readonly string Root = P("web");

    private static string P(params string[] parts) => Path.Combine([Src, .. parts]);

    private static WorktreeLayout Infer(params (string Path, string? Branch)[] wts) =>
        WorktreeLayoutInference.Infer(Root, false, wts.Select(w => new GitWorktree(w.Path, w.Branch)));

    [Fact]
    public void SiblingFoldersNamedRepoDashBranch()
    {
        var l = Infer((P("web-feature-x"), "feature/x"), (P("web-fix-login"), "fix/login"), (P("web-spike"), "spike"));
        Assert.Equal("{parent}/{repo}-{name}", l.Template);
        Assert.Equal(WorktreeLayoutSource.Detected, l.Source);
        Assert.Equal(3, l.Evidence);
        Assert.Equal(P("web-t-0012"), l.Render(Root, "t-0012"));
    }

    [Fact]
    public void SiblingFoldersNamedRepoDotBranch() =>
        Assert.Equal("{parent}/{repo}.{name}", Infer((P("web.feature-x"), "feature/x")).Template);

    [Fact]
    public void NestedFolderWithNestedBranchPaths()
    {
        var l = Infer((P("web", ".worktrees", "feature", "x"), "feature/x"), (P("web", ".worktrees", "main2"), "main2"));
        Assert.Equal("{root}/.worktrees/{name}", l.Template);
        Assert.Equal(P("web", ".worktrees", "t-7"), l.Render(Root, "t-7"));
    }

    [Fact]
    public void ClaudeCodeWorktreesVoteForTheirFolderEvenWhenNamesDontFollowBranches()
    {
        // Claude Code names the folder, not after the branch: only "where it sits" evidence.
        var l = Infer((P("web", ".claude", "worktrees", "brave-otter"), "worktree-brave-otter"),
                      (P("web", ".claude", "worktrees", "calm-heron"), null));
        Assert.Equal("{root}/.claude/worktrees/{name}", l.Template);
    }

    [Fact]
    public void TheMostCommonLayoutWins()
    {
        var l = Infer((P("web-a"), "a"), (P("web-b"), "b"), (P("web", ".worktrees", "c"), "c"));
        Assert.Equal("{parent}/{repo}-{name}", l.Template);
        Assert.Equal(2, l.Evidence);
    }

    [Fact]
    public void LastSegmentAndUnderscoreRenderingsCount()
    {
        Assert.Equal("{parent}/{repo}-{name}", Infer((P("web-x"), "feature/x")).Template);
        Assert.Equal("{parent}/{repo}-{name}", Infer((P("web-feature_x"), "feature/x")).Template);
    }

    [Fact]
    public void ADifferentPrefixIsKeptLiterally() =>
        Assert.Equal("{parent}/wt/{name}", Infer((P("wt", "feature-x"), "feature/x")).Template);

    [Fact]
    public void WorktreesElsewhereDontVoteAndTheDefaultSitsBesideTheRepo()
    {
        var elsewhere = OperatingSystem.IsWindows() ? @"D:\scratch\thing" : "/scratch/thing";
        var l = Infer((elsewhere, "thing"));
        Assert.Equal(WorktreeLayout.DefaultTemplate, l.Template);
        Assert.Equal(WorktreeLayoutSource.Default, l.Source);
        Assert.Equal(P("web.worktrees", "t-0042-fix"), l.Render(Root, "t-0042-fix"));
    }

    [Theory]
    [InlineData("node_modules/\n.worktrees/\n", "{root}/.worktrees/{name}")]
    [InlineData("# comment\n/.claude/worktrees\n", "{root}/.claude/worktrees/{name}")]
    [InlineData("worktrees/*\n", "{root}/worktrees/{name}")]
    [InlineData("bin/\nobj/\n", WorktreeLayout.DefaultTemplate)]
    public void WithNoWorktreesAnIgnoreEntryIsAHint(string gitignore, string template)
    {
        var l = WorktreeLayoutInference.Infer(Root, false, [], gitignore, null);
        Assert.Equal(template, l.Template);
    }

    [Fact]
    public void TheBareLayoutPutsWorktreesBesideBare()
    {
        var root = P("api");
        var detected = WorktreeLayoutInference.Infer(root, true,
            [new GitWorktree(P("api", "main"), "main"), new GitWorktree(P("api", "feature-y"), "feature/y")]);
        Assert.Equal("{root}/{name}", detected.Template);

        var none = WorktreeLayoutInference.Infer(root, true, []);
        Assert.Equal("{root}/{name}", none.Template);
        Assert.Equal(P("api", "t-4"), none.Render(root, "t-4"));
    }

    [Fact]
    public void RenderCarriesALayoutToAnotherRepo()
    {
        var l = new WorktreeLayout("{parent}/{repo}-{name}", WorktreeLayoutSource.Detected);
        Assert.Equal(P("api-t-9"), l.Render(P("api") + Path.DirectorySeparatorChar, "t-9"));
    }
}
