namespace Ochre.Core.Claude;

/// <summary>Which side of a unified-diff line this is, so a viewer can colour it without re-parsing the
/// leading marker. <see cref="Meta"/> is anything that isn't payload — hunk headers, "\ No newline…".</summary>
public enum GitDiffLineKind
{
    Context,
    Added,
    Removed,
    Meta,
}

/// <summary>One line of a unified diff — its <see cref="Kind"/> and text with the leading git marker
/// (<c> </c>/<c>+</c>/<c>-</c>) stripped for payload lines; <see cref="GitDiffLineKind.Meta"/> keeps the
/// whole line.</summary>
public readonly record struct GitDiffLine(GitDiffLineKind Kind, string Text);
