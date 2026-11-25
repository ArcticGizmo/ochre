using System.Text.RegularExpressions;
using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <summary>Plan items and notes agents have changed since the user last reviewed them.</summary>
public sealed record UnreviewedChanges(IReadOnlyList<string> PlanItems, bool Notes);

/// <summary>
/// Works out what agents changed from the task's log, so the review state needs no fields of its own and survives
/// hand edits: every agent entry after the user's last <see cref="ReviewedMessage"/> counts.
/// </summary>
public static partial class AgentChanges
{
    public const string ReviewedMessage = "reviewed agent changes";

    // "2026-11-25T16:23Z session dc59ad95 checked p1", as AgentCommands writes it.
    [GeneratedRegex(@"^\S+ session \S+ (?:checked|unchecked|added) (\S+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PlanEntry();

    [GeneratedRegex(@"^\S+ session \S+ added a note$", RegexOptions.CultureInvariant)]
    private static partial Regex NoteEntry();

    public static UnreviewedChanges Unreviewed(TaskDocument doc)
    {
        var items = new List<string>();
        var notes = false;
        foreach (var entry in doc.Log)
        {
            if (entry.Text.EndsWith(" user " + ReviewedMessage, StringComparison.Ordinal))
            {
                items.Clear();
                notes = false;
            }
            else if (PlanEntry().Match(entry.Text) is { Success: true } m)
            {
                if (!items.Contains(m.Groups[1].Value)) items.Add(m.Groups[1].Value);
            }
            else if (NoteEntry().IsMatch(entry.Text))
            {
                notes = true;
            }
        }
        // An item removed by hand since is nothing to review.
        items.RemoveAll(id => doc.FindItem(id) is null);
        return new UnreviewedChanges(items, notes);
    }
}
