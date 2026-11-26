using System.Text;

namespace Ochre.Core.Terminals;

/// <summary>
/// Filters a terminal's output before it is stored or reaches any window (security.md threat 7, ADR 0011).
///
/// <para>Everything except OSC strings passes through unchanged. An OSC string is collected with the same terminators
/// xterm.js uses (BEL, ST, a bare ESC; CAN, SUB or another C1 control abandons it), checked against an allow list, and
/// re-emitted with a BEL of our own, so xterm parses exactly the sequence that was checked and nothing after it can
/// leak into it. Clipboard writes (OSC 52) and unknown numbers are dropped; OSC 8 links keep only http and https
/// targets.</para>
///
/// <para>An incomplete sequence at the end of a chunk is held back until it completes, so every chunk returned ends
/// on a sequence boundary and the scrollback ring can be trimmed between chunks without cutting one in half.</para>
/// </summary>
public sealed class TerminalOutputFilter
{
    public const int MaxOscLength = 8192;
    public const int MaxLinkLength = 2048;

    // Escapes and CSI sequences are short; one longer than this is junk, and holding it back helps nobody.
    private const int MaxHeld = 256;

    private const char Bel = '\x07', Esc = '\x1b', Can = '\x18', Sub = '\x1a';
    private const char C1Dcs = '\x90', C1Sos = '\x98', C1Csi = '\x9b', C1St = '\x9c', C1Osc = '\x9d', C1Pm = '\x9e', C1Apc = '\x9f';

    // Titles, colours (set and query), the shell's folder, links, ConEmu/Windows Terminal notifications and the
    // shell-integration marks (FinalTerm, VS Code). Anything else, OSC 52 included, is dropped.
    private static readonly HashSet<int> AllowedOsc = [0, 1, 2, 4, 7, 8, 9, 10, 11, 12, 104, 110, 111, 112, 133, 633];

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        Osc,
        // An OSC just ended on ESC: a following '\' completes the ST and is swallowed.
        OscEscape,
        // DCS, SOS, PM or APC: passed through until it ends.
        String,
    }

    private readonly StringBuilder _out = new();
    private readonly StringBuilder _held = new();
    private readonly StringBuilder _osc = new();
    private State _state;
    private bool _oscTooLong;

    /// <summary>The window title the program set (OSC 0 or 2). Display only; never trust it for a decision.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>An absolute folder the shell reported as its current one (OSC 7, or Windows Terminal's OSC 9;9).</summary>
    public event Action<string>? FolderChanged;

    public string Process(ReadOnlySpan<char> input)
    {
        _out.Clear();
        foreach (var c in input) Step(c);
        return _out.ToString();
    }

    /// <summary>The end of the stream: emits a held-back incomplete escape and drops an unfinished OSC.</summary>
    public string Flush()
    {
        _out.Clear();
        if (_state is State.Escape or State.EscapeIntermediate or State.Csi or State.OscEscape)
        {
            if (_state == State.OscEscape) _out.Append(Esc);
            _out.Append(_held);
        }
        _held.Clear();
        _osc.Clear();
        _state = State.Ground;
        return _out.ToString();
    }

    private static bool IsC1(char c) => c is >= '\x80' and <= '\x9f';

    private void Step(char c)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;

            case State.Escape:
                if (c == ']')
                {
                    _held.Clear();
                    StartOsc();
                }
                else if (c == '[')
                {
                    Hold(c, State.Csi);
                }
                else if (c is 'P' or 'X' or '^' or '_')
                {
                    EmitHeld(c);
                    _state = State.String;
                }
                else if (c is >= ' ' and <= '/')
                {
                    Hold(c, State.EscapeIntermediate);
                }
                else
                {
                    EndOfEscape(c);
                }
                break;

            case State.EscapeIntermediate:
                if (c is >= ' ' and <= '/') Hold(c, State.EscapeIntermediate);
                else EndOfEscape(c);
                break;

            case State.Csi:
                if (c is >= '@' and <= '~')
                {
                    EmitHeld(c);
                    _state = State.Ground;
                }
                else if (c == Esc || c == Can || c == Sub || IsC1(c))
                {
                    EndOfEscape(c);
                }
                else
                {
                    Hold(c, State.Csi);
                }
                break;

            case State.Osc:
                Osc(c);
                break;

            case State.OscEscape:
                if (c == '\\')
                {
                    _state = State.Ground;
                }
                else
                {
                    _held.Clear().Append(Esc);
                    _state = State.Escape;
                    Step(c);
                }
                break;

            case State.String:
                if (c == Esc)
                {
                    _held.Clear().Append(Esc);
                    _state = State.Escape;
                }
                else if (c is C1Osc or C1Csi or C1Dcs or C1Sos or C1Pm or C1Apc)
                {
                    _state = State.Ground;
                    Ground(c);
                }
                else
                {
                    _out.Append(c);
                    if (c is C1St or Can or Sub) _state = State.Ground;
                }
                break;
        }
    }

    private void Ground(char c)
    {
        switch (c)
        {
            case Esc:
                _held.Clear().Append(Esc);
                _state = State.Escape;
                break;
            case C1Osc:
                StartOsc();
                break;
            case C1Csi:
                _held.Clear().Append(c);
                _state = State.Csi;
                break;
            case C1Dcs or C1Sos or C1Pm or C1Apc:
                _out.Append(c);
                _state = State.String;
                break;
            default:
                _out.Append(c);
                break;
        }
    }

    // A character that can't continue the escape or CSI held so far. xterm treats ESC, CAN, SUB and C1 controls the
    // same from any state, so they end the held sequence and are handled from ground; C0 controls are executed in
    // place; anything else is the final character.
    private void EndOfEscape(char c)
    {
        if (c == Esc || IsC1(c))
        {
            EmitHeld();
            _state = State.Ground;
            Ground(c);
        }
        else if (c is Can or Sub)
        {
            EmitHeld(c);
            _state = State.Ground;
        }
        else if (c < ' ')
        {
            Hold(c, _state);
        }
        else
        {
            EmitHeld(c);
            _state = State.Ground;
        }
    }

    private void Hold(char c, State next)
    {
        _held.Append(c);
        _state = next;
        if (_held.Length > MaxHeld)
        {
            _out.Append(_held);
            _held.Clear();
        }
    }

    private void EmitHeld(char? final = null)
    {
        _out.Append(_held);
        if (final is { } f) _out.Append(f);
        _held.Clear();
    }

    private void StartOsc()
    {
        _osc.Clear();
        _oscTooLong = false;
        _state = State.Osc;
    }

    private void Osc(char c)
    {
        if (c is Bel or C1St)
        {
            EndOsc();
            _state = State.Ground;
        }
        else if (c == Esc)
        {
            EndOsc();
            _state = State.OscEscape;
        }
        else if (c is Can or Sub)
        {
            _out.Append(c);
            _state = State.Ground;
        }
        else if (IsC1(c))
        {
            _state = State.Ground;
            Ground(c);
        }
        else if (c >= ' ')
        {
            if (_osc.Length < MaxOscLength) _osc.Append(c);
            else _oscTooLong = true;
        }
        // Other C0 controls are ignored inside an OSC, as xterm does.
    }

    private void EndOsc()
    {
        if (_oscTooLong) return;
        var payload = _osc.ToString();
        var semi = payload.IndexOf(';');
        var idText = semi < 0 ? payload : payload[..semi];
        if (idText.Length is 0 or > 4 || !idText.All(char.IsAsciiDigit)) return;
        var id = int.Parse(idText, System.Globalization.CultureInfo.InvariantCulture);
        if (!AllowedOsc.Contains(id)) return;
        var data = semi < 0 ? "" : payload[(semi + 1)..];

        switch (id)
        {
            case 0 or 2:
                TitleChanged?.Invoke(data);
                break;
            case 7:
                if (FolderFromFileUri(data) is { } folder) FolderChanged?.Invoke(folder);
                break;
            case 8:
                payload = "8;" + SafeLink(data);
                break;
            case 9 when data.StartsWith("9;", StringComparison.Ordinal):
                var path = data[2..].Trim('"');
                if (path.Length <= 1024 && Path.IsPathFullyQualified(path)) FolderChanged?.Invoke(path);
                break;
        }

        _out.Append(Esc).Append(']').Append(payload).Append(Bel);
    }

    // OSC 8 is "params;uri". A target that isn't http(s) is replaced by an empty one, which ends any open link, so the
    // text still prints but nothing is clickable.
    private static string SafeLink(string data)
    {
        var semi = data.IndexOf(';');
        if (semi < 0) return ";";
        var parameters = data[..semi];
        var uri = data[(semi + 1)..];
        if (uri.Length == 0 || !IsWebLink(uri)) return ";";
        if (!parameters.All(c => char.IsAsciiLetterOrDigit(c) || c is '=' or ':' or '.' or '_' or '-')) parameters = "";
        return parameters + ";" + uri;
    }

    public static bool IsWebLink(string uri) =>
        uri.Length <= MaxLinkLength
        && !uri.Any(c => c <= ' ' || c == '\x7f')
        && Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    // file://host/C:/path (Windows shells) or file://host/path. The host part names this machine; it isn't checked.
    private static string? FolderFromFileUri(string data)
    {
        if (data.Length > 2048 || !Uri.TryCreate(data, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':') path = path[1..].Replace('/', '\\');
        return path.Length <= 1024 && Path.IsPathFullyQualified(path) ? path : null;
    }
}
