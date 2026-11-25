using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Ochre.Core.Tasks;

/// <summary>
/// One task file: YAML frontmatter plus a markdown body with Plan, Notes and Log sections (docs/task-model.md).
/// Edits are surgical so hand-written content survives: body changes touch only the lines they mean to, and the
/// frontmatter is re-emitted only when a field changes (which drops YAML comments in it, the one known loss).
/// </summary>
public sealed partial class TaskDocument
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    private readonly YamlMappingNode _front;
    private readonly List<string> _body;
    private readonly string _newline;
    private string _rawFront;
    private bool _frontDirty;

    private TaskDocument(YamlMappingNode front, string rawFront, List<string> body, string newline)
    {
        _front = front;
        _rawFront = rawFront;
        _body = body;
        _newline = newline;
    }

    public static TaskDocument Parse(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count == 0 || lines[0].TrimEnd() != "---") throw new TaskFormatException("missing frontmatter");

        var close = lines.FindIndex(1, l => l.TrimEnd() == "---");
        if (close < 0) throw new TaskFormatException("unterminated frontmatter");

        var rawFront = string.Join(newline, lines.Skip(1).Take(close - 1));
        YamlMappingNode front;
        try
        {
            var stream = new YamlStream();
            // The trailing newline matters: without it a literal block (note: |) at the end loses its final line
            // break and re-emits as |-.
            stream.Load(new StringReader(rawFront + "\n"));
            front = stream.Documents.Count == 0
                ? new YamlMappingNode()
                : stream.Documents[0].RootNode as YamlMappingNode ?? throw new TaskFormatException("frontmatter is not a mapping");
        }
        catch (YamlException ex)
        {
            throw new TaskFormatException($"invalid frontmatter: {ex.Message}");
        }

        return new TaskDocument(front, rawFront, lines.Skip(close + 1).ToList(), newline);
    }

    public static TaskDocument CreateNew(string id, string title, DateTimeOffset now)
    {
        var doc = new TaskDocument(new YamlMappingNode(), "", ["", "## Plan", "", "## Notes", "", "## Log", ""], "\n");
        doc.Id = id;
        doc.Title = title;
        doc.State = TaskState.Backlog;
        doc.SetTimestamp("created", now);
        doc.Updated = now;
        return doc;
    }

    public string Serialize()
    {
        if (_frontDirty)
        {
            _rawFront = EmitFront();
            _frontDirty = false;
        }
        var sb = new StringBuilder();
        sb.Append("---").Append(_newline);
        if (_rawFront.Length > 0) sb.Append(_rawFront).Append(_newline);
        sb.Append("---").Append(_newline);
        sb.Append(string.Join(_newline, _body));
        return sb.ToString();
    }

    private string EmitFront()
    {
        var writer = new StringWriter { NewLine = _newline };
        var settings = new EmitterSettings().WithIndentedSequences().WithBestWidth(int.MaxValue).WithNewLine(_newline);
        new YamlStream(new YamlDocument(_front)).Save(new Emitter(writer, settings), assignAnchors: false);
        // YamlStream writes a document end marker; frontmatter has its own delimiters.
        var text = writer.ToString().TrimEnd();
        if (text.EndsWith("...", StringComparison.Ordinal)) text = text[..^3].TrimEnd();
        return text == "{}" ? "" : text;
    }

    // ---- frontmatter fields -------------------------------------------------------------------------------------

    public string Id
    {
        get => GetString("id") ?? "";
        set => SetString("id", value);
    }

    public string Title
    {
        get => GetString("title") ?? "";
        set => SetString("title", SingleLine(value));
    }

    public TaskState State
    {
        get => Enum.TryParse<TaskState>(GetString("state"), ignoreCase: true, out var s) ? s : TaskState.Backlog;
        set => SetString("state", value.ToString().ToLowerInvariant());
    }

    public DateTimeOffset? Created => GetTimestamp("created");

    public DateTimeOffset? Updated
    {
        get => GetTimestamp("updated");
        set
        {
            if (value is { } v) SetTimestamp("updated", v);
        }
    }

    public int? Order
    {
        get => int.TryParse(GetString("order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) ? o : null;
        set
        {
            if (value is { } v) SetString("order", v.ToString(CultureInfo.InvariantCulture));
            else Remove("order");
        }
    }

    public IReadOnlyList<string> Repos =>
        _front.Children.TryGetValue(new YamlScalarNode("repos"), out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").Where(s => s.Length > 0).ToList()
            : [];

    public void SetRepos(IEnumerable<string> repos)
    {
        var seq = new YamlSequenceNode(repos.Select(r => (YamlNode)new YamlScalarNode(SingleLine(r))));
        if (seq.Children.Count == 0) Remove("repos");
        else Set("repos", seq);
    }

    public IReadOnlyList<TaskLink> Links => Mappings("links")
        .Select(m => new TaskLink(Scalar(m, "id") ?? "", Scalar(m, "kind") ?? "", Scalar(m, "ref") ?? "", Scalar(m, "title"), Scalar(m, "summary")))
        .ToList();

    public IReadOnlyList<TaskSessionRef> Sessions => Mappings("sessions")
        .Select(m => new TaskSessionRef(Scalar(m, "id") ?? "", Scalar(m, "cwd") ?? "", Scalar(m, "configDir"), Scalar(m, "binding") ?? "launched", Scalar(m, "role")))
        .ToList();

    public void AddSession(TaskSessionRef session)
    {
        var map = new YamlMappingNode { { "id", session.Id }, { "cwd", session.Cwd } };
        if (session.ConfigDir is { } dir) map.Add("configDir", dir);
        map.Add("binding", session.Binding);
        if (session.Role is { } role) map.Add("role", role);
        SequenceFor("sessions").Add(map);
        _frontDirty = true;
    }

    public ParkNote? Park
    {
        get
        {
            if (!_front.Children.TryGetValue(new YamlScalarNode("park"), out var node) || node is not YamlMappingNode m) return null;
            var note = Scalar(m, "note");
            return note is null ? null : new ParkNote(ParseTimestamp(Scalar(m, "at")) ?? DateTimeOffset.MinValue, note.TrimEnd());
        }
        set
        {
            if (value is null)
            {
                Remove("park");
                return;
            }
            var note = new YamlScalarNode(value.Note.TrimEnd() + "\n") { Style = ScalarStyle.Literal };
            Set("park", new YamlMappingNode { { "at", Format(value.At) }, { new YamlScalarNode("note"), note } });
        }
    }

    // ---- body: plan ---------------------------------------------------------------------------------------------

    [GeneratedRegex(@"^(?<indent>[ \t]*)[-*] \[(?<mark>[ xX])\] (?<text>.*?)(?:\s*\{#(?<id>[\w-]+)\})?\s*$")]
    private static partial Regex PlanLine();

    public IReadOnlyList<PlanItem> Plan
    {
        get
        {
            if (FindSection("Plan") is not var (start, end)) return [];

            // Collect checkbox lines with their indents, then nest by indentation.
            var flat = new List<(string? Id, string Text, bool Done, int Indent, int Line)>();
            for (var i = start + 1; i < end; i++)
            {
                var m = PlanLine().Match(_body[i]);
                if (!m.Success) continue;
                flat.Add((m.Groups["id"].Success ? m.Groups["id"].Value : null, m.Groups["text"].Value, m.Groups["mark"].Value != " ", IndentWidth(m.Groups["indent"].Value), i));
            }
            return Nest(flat, 0, flat.Count);
        }
    }

    private static List<PlanItem> Nest(List<(string? Id, string Text, bool Done, int Indent, int Line)> flat, int from, int to)
    {
        var items = new List<PlanItem>();
        var i = from;
        while (i < to)
        {
            var (id, text, done, indent, line) = flat[i];
            var j = i + 1;
            while (j < to && flat[j].Indent > indent) j++;
            items.Add(new PlanItem(id, text, done, indent, line, Nest(flat, i + 1, j)));
            i = j;
        }
        return items;
    }

    public IEnumerable<PlanItem> AllPlanItems() => Plan.SelectMany(p => p.SelfAndDescendants());

    public PlanItem? FindItem(string id) => AllPlanItems().FirstOrDefault(p => p.Id == id);

    /// <summary>Returns false when there's no item with that id, or it was already in that state.</summary>
    public bool SetItemDone(string id, bool done)
    {
        if (FindItem(id) is not { } item || item.Done == done) return false;
        var line = _body[item.Line];
        var mark = line.IndexOf('[', StringComparison.Ordinal);
        _body[item.Line] = line[..(mark + 1)] + (done ? "x" : " ") + line[(mark + 2)..];
        return true;
    }

    /// <summary>Adds a plan item at the end of the plan, or as the last child of <paramref name="parentId"/>.</summary>
    public string AddItem(string text, string? parentId = null)
    {
        text = SingleLine(text).Replace("{#", "{ #", StringComparison.Ordinal);
        if (text.Length == 0) throw new ArgumentException("plan item text is empty", nameof(text));
        var id = NextItemId();

        if (parentId is not null)
        {
            var parent = FindItem(parentId) ?? throw new KeyNotFoundException($"no plan item '{parentId}'");
            var last = parent.SelfAndDescendants().Max(p => p.Line);
            var indent = new string(' ', parent.Indent + 2);
            _body.Insert(last + 1, $"{indent}- [ ] {text} {{#{id}}}");
            return id;
        }

        var (start, end) = FindSection("Plan") ?? CreateSection("Plan", before: "Notes");
        var items = AllPlanItems().ToList();
        var at = items.Count > 0 ? items.Max(p => p.Line) + 1 : TrimmedEnd(start, end);
        _body.Insert(at, $"- [ ] {text} {{#{id}}}");
        return id;
    }

    /// <summary>Gives every plan item a unique id, so agents can address items by id rather than position.</summary>
    public bool EnsureItemIds()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        foreach (var item in AllPlanItems().OrderBy(p => p.Line))
        {
            if (item.Id is { } id && seen.Add(id)) continue;
            var newId = NextItemId(seen);
            seen.Add(newId);
            var line = _body[item.Line];
            var withoutId = item.Id is null ? line.TrimEnd() : Regex.Replace(line, @"\s*\{#[\w-]+\}\s*$", "");
            _body[item.Line] = $"{withoutId} {{#{newId}}}";
            changed = true;
        }
        return changed;
    }

    private string NextItemId(HashSet<string>? taken = null)
    {
        var max = AllPlanItems().Select(p => p.Id).Concat(taken ?? []).Select(NumericSuffix).DefaultIfEmpty(0).Max();
        return $"p{max + 1}";

        static int NumericSuffix(string? id) =>
            id is { Length: > 1 } && id[0] == 'p' && int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    // ---- body: notes and log ------------------------------------------------------------------------------------

    public string Notes
    {
        get
        {
            if (FindSection("Notes") is not var (start, end)) return "";
            return string.Join("\n", _body.Skip(start + 1).Take(end - start - 1)).Trim();
        }
    }

    public void SetNotes(string notes)
    {
        var (start, end) = FindSection("Notes") ?? CreateSection("Notes", before: "Log");
        _body.RemoveRange(start + 1, end - start - 1);
        var lines = new List<string> { "" };
        if (notes.Trim().Length > 0)
        {
            lines.AddRange(notes.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(EscapeHeading));
            lines.Add("");
        }
        _body.InsertRange(start + 1, lines);
    }

    public void AppendNote(string note)
    {
        var current = Notes;
        SetNotes(current.Length == 0 ? note : current + "\n\n" + note.Trim());
    }

    public IReadOnlyList<LogEntry> Log
    {
        get
        {
            if (FindSection("Log") is not var (start, end)) return [];
            return _body.Skip(start + 1).Take(end - start - 1).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => new LogEntry(l[2..])).ToList();
        }
    }

    public void AppendLog(DateTimeOffset at, string author, string message)
    {
        var (start, end) = FindSection("Log") ?? CreateSection("Log", before: null);
        var line = $"- {at.UtcDateTime.ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture)} {SingleLine(author)} {SingleLine(message)}";
        _body.Insert(TrimmedEnd(start, end), line);
    }

    // ---- summary ------------------------------------------------------------------------------------------------

    public TaskSummary ToSummary(string id)
    {
        var items = AllPlanItems().ToList();
        return new TaskSummary(
            id,
            Title,
            State,
            Updated,
            Order,
            items.Count(i => i.Done),
            items.Count,
            items.FirstOrDefault(i => !i.Done)?.Text,
            Park?.Note);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    /// <summary>The heading line index and the exclusive end of a "## Name" section.</summary>
    private (int Start, int End)? FindSection(string name)
    {
        var start = _body.FindIndex(l => IsHeading(l, out var title) && title.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return null;
        var end = _body.FindIndex(start + 1, l => IsHeading(l, out _));
        return (start, end < 0 ? _body.Count : end);
    }

    private static bool IsHeading(string line, out string title)
    {
        title = "";
        if (!line.StartsWith("## ", StringComparison.Ordinal) && !line.StartsWith("# ", StringComparison.Ordinal)) return false;
        title = line.TrimStart('#').Trim();
        return true;
    }

    private (int Start, int End) CreateSection(string name, string? before)
    {
        var at = before is not null && FindSection(before) is var (s, _) ? s : _body.Count;
        // Trailing empty string means the file ended with a newline; keep it last.
        if (before is null && _body.Count > 0 && _body[^1].Length == 0) at = _body.Count - 1;
        var lines = new List<string>();
        if (at > 0 && _body[at - 1].Trim().Length > 0) lines.Add("");
        lines.Add($"## {name}");
        lines.Add("");
        _body.InsertRange(at, lines);
        var start = at + lines.Count - 2;
        return (start, start + 2);
    }

    /// <summary>The index just after the last non-blank line of a section, where appended content goes.</summary>
    private int TrimmedEnd(int start, int end)
    {
        var i = end;
        while (i > start + 1 && _body[i - 1].Trim().Length == 0) i--;
        return i == start + 1 && end > start + 1 ? start + 2 : i;
    }

    private static string EscapeHeading(string line) => line.StartsWith('#') ? "\\" + line : line;

    private static string SingleLine(string value) => Regex.Replace(value, @"[\r\n]+", " ").Trim();

    private static int IndentWidth(string indent) => indent.Sum(c => c == '\t' ? 4 : 1);

    private string? GetString(string key) =>
        _front.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode s ? s.Value : null;

    private void SetString(string key, string value)
    {
        if (GetString(key) == value) return;
        Set(key, new YamlScalarNode(value));
    }

    private void Set(string key, YamlNode node)
    {
        _front.Children[new YamlScalarNode(key)] = node;
        _frontDirty = true;
    }

    private void Remove(string key)
    {
        if (_front.Children.Remove(new YamlScalarNode(key))) _frontDirty = true;
    }

    private YamlSequenceNode SequenceFor(string key)
    {
        if (_front.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode seq) return seq;
        seq = new YamlSequenceNode();
        Set(key, seq);
        return seq;
    }

    private IEnumerable<YamlMappingNode> Mappings(string key) =>
        _front.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlMappingNode>()
            : [];

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode s ? s.Value : null;

    private DateTimeOffset? GetTimestamp(string key) => ParseTimestamp(GetString(key));

    private void SetTimestamp(string key, DateTimeOffset value) => SetString(key, Format(value));

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
