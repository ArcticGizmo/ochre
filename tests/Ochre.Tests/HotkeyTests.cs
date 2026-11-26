using Ochre.Core.Hosting;
using Xunit;

namespace Ochre.Tests;

/// <summary>The jump shortcut as typed into Settings or settings.json: read in any case and order, written one way.</summary>
public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData(" alt + control + j ", "Ctrl+Alt+J")]
    [InlineData("Win+Shift+o", "Shift+Win+O")]
    [InlineData("ctrl+f12", "Ctrl+F12")]
    [InlineData("Alt+7", "Alt+7")]
    public void Reads_a_combination_and_writes_it_one_way(string text, string expected)
    {
        var hotkey = Hotkey.Parse(text, out var error);
        Assert.Null(error);
        Assert.Equal(expected, hotkey!.ToString());
        Assert.Equal(hotkey, Hotkey.Parse(hotkey.ToString(), out _));
    }

    [Theory]
    [InlineData("", "Add a key")]
    [InlineData("Ctrl+Alt", "Add a key")]
    [InlineData("Shift+J", "Include Ctrl, Alt or Win")]
    [InlineData("J", "Include Ctrl, Alt or Win")]
    [InlineData("Ctrl+J+K", "one key")]
    [InlineData("Ctrl+Tab", "isn't a key")]
    [InlineData("Ctrl+F25", "isn't a key")]
    [InlineData("Ctrl+F0", "isn't a key")]
    public void Refuses_what_it_cant_register_or_would_take_typing(string text, string reason)
    {
        Assert.Null(Hotkey.Parse(text, out var error));
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }
}
