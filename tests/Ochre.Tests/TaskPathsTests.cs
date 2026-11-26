using Ochre.Core.Files;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The editor reads and writes only inside a task's folders (ADR 0015, security.md threat 5): whatever the renderer
/// sends, a path either lands inside the folder by its real name or is refused.
/// </summary>
public sealed class TaskPathsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-paths-").FullName;

    public void Dispose() => GitRunnerTests.DeleteTree(_dir);

    private string Folder(string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "src", "Deep"));
        File.WriteAllText(Path.Combine(folder, "src", "Deep", "File.cs"), "x");
        Directory.CreateDirectory(Path.Combine(folder, ".git", "hooks"));
        return folder;
    }

    [Fact]
    public void A_plain_relative_path_resolves_inside_the_folder_by_its_real_name()
    {
        var folder = Folder("repo");

        Assert.True(TaskPaths.TryResolve(folder, "src/deep/file.cs", out var full, out _));
        Assert.Equal(Path.Combine(folder, "src", "Deep", "File.cs"), full);

        Assert.True(TaskPaths.TryResolve(folder, "src\\Deep\\New.cs", out var missing, out _));
        Assert.Equal(Path.Combine(folder, "src", "Deep", "New.cs"), missing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:x.txt")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("..\\outside.txt")]
    [InlineData("src/../../outside.txt")]
    [InlineData("src/./Deep/File.cs")]
    [InlineData("src//Deep")]
    [InlineData("src/Deep/File.cs::$DATA")]
    [InlineData("src/Deep/File.cs:stream")]
    [InlineData("CON")]
    [InlineData("src/nul.txt")]
    [InlineData("Com1.log")]
    [InlineData("src.")]
    [InlineData("src ")]
    [InlineData("src/*.cs")]
    [InlineData("a\u0001b")]
    [InlineData(".git/config")]
    [InlineData(".GIT/hooks/pre-commit")]
    [InlineData("sub/.git/hooks/pre-commit")]
    public void Anything_but_a_plain_path_below_the_folder_is_refused(string path)
    {
        var folder = Folder("repo");
        Assert.False(TaskPaths.TryResolve(folder, path, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_link_inside_the_folder_is_refused_rather_than_followed()
    {
        var folder = Folder("repo");
        var outside = Directory.CreateDirectory(Path.Combine(_dir, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "x");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "link"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Symlinks need Developer Mode or elevation on Windows.
        }

        Assert.False(TaskPaths.TryResolve(folder, "link/secret.txt", out _, out var error));
        Assert.Contains("link", error);
    }

    [Fact]
    public void A_short_name_for_git_is_refused()
    {
        var folder = Folder("repo");
        // Only where the volume makes 8.3 names; elsewhere there's nothing to alias.
        if (!Directory.Exists(Path.Combine(folder, "GIT~1"))) return;
        Assert.False(TaskPaths.TryResolve(folder, "GIT~1/hooks/pre-commit", out _, out _));
    }

    [Fact]
    public void Locate_finds_the_deepest_task_folder_and_nothing_outside()
    {
        var repo = Folder("repo");
        var worktree = Directory.CreateDirectory(Path.Combine(repo, "wt")).FullName;
        var doc = TaskDocument.CreateNew("T-0001", "t", DateTimeOffset.UtcNow);
        doc.SetRepos([repo]);
        doc.AddWorktree(new TaskWorktree(worktree, repo, "git-worktree", "wt", null));

        Assert.Equal((repo, "src/Deep/File.cs"), TaskPaths.Locate(doc, Path.Combine(repo, "src", "Deep", "File.cs")));
        Assert.Equal((worktree, "a.txt"), TaskPaths.Locate(doc, Path.Combine(worktree, "a.txt")));
        Assert.Null(TaskPaths.Locate(doc, repo + "2\\x.txt"));
        Assert.Null(TaskPaths.Locate(doc, Path.Combine(_dir, "other.txt")));
        Assert.Null(TaskPaths.Locate(doc, "src/Deep/File.cs"));
        Assert.Null(TaskPaths.Locate(doc, Path.Combine(repo, ".git", "config")));
    }
}
