using System.Text.RegularExpressions;

namespace Ochre.Core.Hosting;

/// <summary>
/// A system-wide key combination, written the way people say it: <c>Ctrl+Alt+Space</c>. At least one of Ctrl, Alt or
/// Win, so it never takes a key people type; the key is a letter, a digit, F1 to F24 or Space.
/// </summary>
public sealed partial record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    public const string DefaultJump = "Ctrl+Alt+Space";

    /// <summary>Reads a combination in any case and order, e.g. <c>alt + ctrl + j</c>; null with a reason when it isn't one.</summary>
    public static Hotkey? Parse(string text, out string? error)
    {
        error = null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    ctrl = true;
                    break;
                case "ALT":
                    alt = true;
                    break;
                case "SHIFT":
                    shift = true;
                    break;
                case "WIN" or "WINDOWS":
                    win = true;
                    break;
                default:
                    if (key is not null)
                    {
                        error = "Use one key with Ctrl, Alt, Shift or Win, e.g. Ctrl+Alt+Space.";
                        return null;
                    }
                    key = KeyName(part);
                    if (key is null)
                    {
                        error = $"\"{part}\" isn't a key Ochre can use: a letter, a digit, F1 to F24 or Space.";
                        return null;
                    }
                    break;
            }
        }
        if (key is null)
        {
            error = "Add a key, e.g. Ctrl+Alt+Space.";
            return null;
        }
        if (!ctrl && !alt && !win)
        {
            error = "Include Ctrl, Alt or Win, so the shortcut doesn't take a key you type.";
            return null;
        }
        return new Hotkey(ctrl, alt, shift, win, key);
    }

    public override string ToString() =>
        string.Join('+', new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Win ? "Win" : null, Key }.OfType<string>());

    private static string? KeyName(string part)
    {
        var upper = part.ToUpperInvariant();
        if (upper == "SPACE") return "Space";
        if (upper.Length == 1 && (char.IsAsciiLetterUpper(upper[0]) || char.IsAsciiDigit(upper[0]))) return upper;
        return FunctionKey().IsMatch(upper) && int.Parse(upper[1..], System.Globalization.CultureInfo.InvariantCulture) is >= 1 and <= 24 ? upper : null;
    }

    [GeneratedRegex("^F[0-9]{1,2}$")]
    private static partial Regex FunctionKey();
}
