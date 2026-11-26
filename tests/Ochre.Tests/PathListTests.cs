using Ochre.Core.Platform;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Installing or uninstalling Ochre edits the user's PATH. The editor works on the raw registry string, so it never
/// expands anyone's <c>%VAR%</c> entries for good and leaves what it doesn't own byte for byte alone.
/// </summary>
public sealed class PathListTests
{
    private const string Dir = @"C:\Users\A\AppData\Local\OchreApp\current";

    // A stand-in for Environment.ExpandEnvironmentVariables with fixed values.
    private static string Expand(string s) => s
        .Replace("%LOCALAPPDATA%", @"C:\Users\A\AppData\Local", StringComparison.OrdinalIgnoreCase)
        .Replace("%USERPROFILE%", @"C:\Users\A", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Add_appends_and_keeps_variables_unexpanded()
    {
        // Only the trailing separator is folded; the padded entry is kept as written.
        Assert.Equal(@"%USERPROFILE%\bin; C:\tools ;" + Dir, PathList.WithEntry(@"%USERPROFILE%\bin; C:\tools ;", Dir, Expand));
    }

    [Fact]
    public void Add_to_an_empty_path_is_just_the_dir() =>
        Assert.Equal(Dir, PathList.WithEntry("", Dir, Expand));

    [Theory]
    [InlineData(@"C:\x;C:\Users\A\AppData\Local\OchreApp\current")]
    [InlineData(@"C:\x;c:\users\a\appdata\local\ochreapp\CURRENT\")]           // case and a trailing slash
    [InlineData(@"C:\x;%LOCALAPPDATA%\OchreApp\current")]                      // written with a variable
    [InlineData(@"C:\x; C:\Users\A\AppData\Local\OchreApp\current ;C:\y")]     // padded
    public void Add_is_a_no_op_when_already_present(string current) =>
        Assert.Null(PathList.WithEntry(current, Dir, Expand));

    [Fact]
    public void Remove_drops_only_our_entry_and_leaves_the_rest_as_written()
    {
        var current = @"%USERPROFILE%\bin;;  C:\tools  ;%LOCALAPPDATA%\OchreApp\current;C:\Users\A\AppData\Local\OchreApp\current\;C:\last";
        Assert.Equal(@"%USERPROFILE%\bin;;  C:\tools  ;C:\last", PathList.WithoutEntry(current, Dir, Expand));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"%USERPROFILE%\bin;C:\tools")]
    [InlineData(@"C:\Users\A\AppData\Local\OchreApp\current-old")]              // a prefix is not a match
    public void Remove_is_a_no_op_when_absent(string current) =>
        Assert.Null(PathList.WithoutEntry(current, Dir, Expand));
}
