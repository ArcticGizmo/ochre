using System.Text;
using Ochre.Core.Files;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Saving from the editor never loses someone else's change or reshapes the file (ADR 0015): a save names the version
/// it edited, and keeps the BOM and the line endings it was given.
/// </summary>
public sealed class TextFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-text-").FullName;

    public void Dispose() => GitRunnerTests.DeleteTree(_dir);

    private string Make(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void A_save_from_a_stale_version_is_refused_and_leaves_the_file_alone()
    {
        var path = Make("a.txt", Encoding.UTF8.GetBytes("one\n"));
        var mine = TextFiles.Read(path);
        File.WriteAllText(path, "agent's edit\n");

        var result = TextFiles.Write(path, "my edit\n", mine.Version);

        Assert.False(result.Saved);
        Assert.Equal(TextFiles.Read(path).Version, result.Version);
        Assert.Equal("agent's edit\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_save_from_the_current_version_writes_and_returns_the_new_version()
    {
        var path = Make("a.txt", Encoding.UTF8.GetBytes("one\n"));
        var read = TextFiles.Read(path);

        var result = TextFiles.Write(path, "two\n", read.Version);

        Assert.True(result.Saved);
        Assert.Equal("two\n", File.ReadAllText(path));
        Assert.Equal(TextFiles.Read(path).Version, result.Version);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void The_bom_and_the_line_endings_sent_are_kept()
    {
        var path = Make("b.cs", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\r\nb\r\n")]);
        var read = TextFiles.Read(path);
        Assert.True(read.Bom);
        Assert.Equal("a\r\nb\r\n", read.Text);

        Assert.True(TextFiles.Write(path, "a\r\nb\r\nc\r\n", read.Version).Saved);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\r\nb\r\nc\r\n")], File.ReadAllBytes(path));
    }

    [Fact]
    public void A_deleted_file_is_not_recreated()
    {
        var path = Make("gone.txt", Encoding.UTF8.GetBytes("x"));
        var read = TextFiles.Read(path);
        File.Delete(path);

        var result = TextFiles.Write(path, "y", read.Version);

        Assert.False(result.Saved);
        Assert.Equal("", result.Version);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Binary_and_non_utf8_files_are_not_editable()
    {
        var binary = TextFiles.Read(Make("x.bin", [1, 2, 0, 3]));
        Assert.Equal(TextFileProblem.Binary, binary.Problem);
        Assert.Null(binary.Text);

        var latin1 = Make("l.txt", [0x63, 0x61, 0x66, 0xE9]);
        var read = TextFiles.Read(latin1);
        Assert.Equal(TextFileProblem.NotUtf8, read.Problem);
        Assert.True(read.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => TextFiles.Write(latin1, "cafe", read.Version));
        Assert.Equal([0x63, 0x61, 0x66, 0xE9], File.ReadAllBytes(latin1));
    }

    [Fact]
    public void A_file_too_big_to_save_opens_read_only()
    {
        var path = Make("big.txt", Encoding.UTF8.GetBytes(new string('x', (int)TextFiles.MaxEditBytes + 1)));
        var read = TextFiles.Read(path);
        Assert.Equal(TextFileProblem.None, read.Problem);
        Assert.True(read.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => TextFiles.Write(path, "x", read.Version));
    }
}
