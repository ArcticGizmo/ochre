using Ochre.Core.Terminals;
using Xunit;

namespace Ochre.Tests;

// Security boundary (security.md threat 7, ADR 0011): what reaches xterm.js and the saved scrollback.
// Control characters are written as fixed-width \u escapes: C#'s \x takes up to four hex digits, so "\x07a" is one character.
public class TerminalOutputFilterTests
{
    private const string St = "\u001b\\";

    private static string Run(params string[] chunks)
    {
        var filter = new TerminalOutputFilter();
        return string.Concat(chunks.Select(c => filter.Process(c))) + filter.Flush();
    }

    [Theory]
    [InlineData("plain text\r\n")]
    [InlineData("\u001b[31mred\u001b[0m and \u001b[1;38;2;10;20;30mtruecolour\u001b[0m")]
    [InlineData("\u001b[?1049h\u001b[2J\u001b[H full screen \u001b[?1049l")]
    [InlineData("héllo → 世界 🙂")]
    [InlineData("\u001b(B\u001b)0\u001b7\u001b8\u001bM")]
    public void Ordinary_output_passes_unchanged(string text) => Assert.Equal(text, Run(text));

    [Theory]
    [InlineData("\u001b]52;c;ZXZpbA==\u0007")]
    [InlineData("\u001b]52;c;ZXZpbA==\u001b\\")]
    [InlineData("\u009d52;c;ZXZpbA==\u009c")]
    [InlineData("\u001b]52;c;ZXZpbA==\u009c")]
    [InlineData("\u001b]52;c;?\u0007")]
    public void Clipboard_writes_are_dropped(string osc52) => Assert.Equal("beforeafter", Run("before" + osc52 + "after"));

    [Fact]
    public void Clipboard_write_split_across_reads_is_dropped() =>
        Assert.Equal("ab", Run("a\u001b", "]5", "2;c;ZXZp", "bA==\u001b", "\\b"));

    [Fact]
    public void Unknown_osc_numbers_are_dropped() =>
        Assert.Equal("xy", Run("x\u001b]1337;File=inline=1:AAAA\u0007y"));

    [Fact]
    public void Non_numeric_osc_is_dropped() => Assert.Equal("xy", Run("x\u001b]evil;payload\u0007y"));

    [Fact]
    public void Titles_pass_and_are_reported_with_our_terminator()
    {
        var filter = new TerminalOutputFilter();
        string? title = null;
        filter.TitleChanged += t => title = t;
        Assert.Equal("\u001b]0;build: ok\u0007after", filter.Process("\u001b]0;build: ok" + St + "after"));
        Assert.Equal("build: ok", title);
    }

    [Fact]
    public void Http_links_are_kept()
    {
        var link = "\u001b]8;id=a1;https://example.com/docs?q=1\u0007";
        var end = "\u001b]8;;\u0007";
        Assert.Equal(link + "docs" + end, Run(link + "docs" + end));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("vscode://some.extension/run")]
    [InlineData("https://exa mple.com")]
    [InlineData("//example.com/relative")]
    public void Other_link_targets_lose_the_link_but_keep_the_text(string uri) =>
        Assert.Equal("\u001b]8;;\u0007click me\u001b]8;;\u0007", Run($"\u001b]8;;{uri}\u0007click me\u001b]8;;\u0007"));

    [Fact]
    public void Overlong_link_target_is_dropped()
    {
        var uri = "https://example.com/" + new string('a', TerminalOutputFilter.MaxLinkLength);
        Assert.Equal("\u001b]8;;\u0007x", Run($"\u001b]8;;{uri}\u0007x"));
    }

    [Fact]
    public void Odd_link_params_are_cleared_but_the_safe_link_kept() =>
        Assert.Equal("\u001b]8;;https://example.com\u0007", Run("\u001b]8;id=\"x\"<b>;https://example.com\u0007"));

    [Fact]
    public void Cancel_abandons_an_osc_and_what_follows_prints()
    {
        Assert.Equal("\u0018after", Run("\u001b]52;c;ZXZpbA==\u0018after"));
        Assert.Equal("\u001aafter", Run("\u001b]0;title\u001aafter"));
    }

    [Fact]
    public void A_c1_control_inside_an_osc_ends_it_before_anything_after_can_join()
    {
        // As far as the filter is concerned the target ends at the C1 control, so the text after it can't become
        // part of the target xterm sees.
        var output = Run("\u001b]8;;https://ok.example\u0085javascript:alert(1)\u0007");
        Assert.DoesNotContain("\u001b]8;;https://ok.example", output);
        Assert.DoesNotContain("\u001b]8;;javascript", output);
    }

    [Fact]
    public void Osc_started_inside_another_string_is_still_filtered()
    {
        // DCS passthrough, then a C1 OSC: xterm leaves the DCS and starts the OSC, so the filter must too.
        Assert.Equal("\u001bPq#0\u009cx", Run("\u001bPq#0\u009c\u009d52;c;ZXZpbA==\u0007x"));
        Assert.Equal("\u001bPq#0x", Run("\u001bPq#0\u009d52;c;ZXZpbA==\u0007x"));
        Assert.Equal("\u001b_apc\u001b\\x", Run("\u001b_apc\u001b\\\u001b]52;c;ZXZpbA==\u0007x"));
    }

    [Fact]
    public void Escape_with_an_intermediate_then_bracket_is_not_an_osc()
    {
        // ESC ( ] is a charset designation, so xterm prints what follows; the filter leaves it alone too.
        var text = "\u001b(]52;c;ZXZpbA==\u0007";
        Assert.Equal(text, Run(text));
    }

    [Fact]
    public void Overlong_osc_is_dropped_whole_and_the_stream_recovers()
    {
        var huge = "\u001b]0;" + new string('x', TerminalOutputFilter.MaxOscLength + 10) + "\u0007";
        Assert.Equal("ab", Run("a" + huge + "b"));
    }

    [Fact]
    public void C0_controls_inside_an_osc_are_ignored_as_xterm_does() =>
        Assert.Equal("\u001b]0;ab\u0007", Run("\u001b]0;a\u0001\u000db\u0007"));

    [Fact]
    public void Incomplete_sequences_are_held_until_complete()
    {
        var filter = new TerminalOutputFilter();
        Assert.Equal("abc", filter.Process("abc\u001b[3"));
        Assert.Equal("\u001b[31mred", filter.Process("1mred"));
        Assert.Equal("", filter.Process("\u001b"));
        Assert.Equal("\u001b[0m", filter.Process("[0m"));
        Assert.Equal("", filter.Process("\u001b]0;ti"));
        Assert.Equal("\u001b]0;title\u0007", filter.Process("tle\u0007"));
    }

    [Fact]
    public void Flush_emits_a_held_escape_and_drops_an_unfinished_osc()
    {
        var filter = new TerminalOutputFilter();
        filter.Process("x\u001b[1");
        Assert.Equal("\u001b[1", filter.Flush());
        filter.Process("\u001b]52;c;ZXZp");
        Assert.Equal("", filter.Flush());
    }

    [Theory]
    [InlineData("\u001b]7;file://host/C:/Users/me/repo\u0007", @"C:\Users\me\repo")]
    [InlineData("\u001b]7;file://localhost/C:/with%20space\u0007", @"C:\with space")]
    [InlineData("\u001b]9;9;\"C:\\work\\app\"\u0007", @"C:\work\app")]
    [InlineData("\u001b]9;9;C:\\work\u0007", @"C:\work")]
    public void The_shell_folder_is_reported(string osc, string expected)
    {
        var filter = new TerminalOutputFilter();
        string? folder = null;
        filter.FolderChanged += f => folder = f;
        filter.Process(osc);
        Assert.Equal(expected, folder);
    }

    [Theory]
    [InlineData("\u001b]7;relative/path\u0007")]
    [InlineData("\u001b]7;https://example.com/C:/x\u0007")]
    [InlineData("\u001b]9;9;not\\absolute\u0007")]
    public void Odd_folders_are_not_reported(string osc)
    {
        var filter = new TerminalOutputFilter();
        string? folder = null;
        filter.FolderChanged += f => folder = f;
        filter.Process(osc);
        Assert.Null(folder);
    }
}
