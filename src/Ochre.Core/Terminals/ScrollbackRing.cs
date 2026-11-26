using System.Text;

namespace Ochre.Core.Terminals;

/// <summary>
/// The tail of a terminal's filtered output. Trimmed by whole chunks, which the filter ends on sequence boundaries,
/// so replaying it never starts inside an escape sequence. Not thread-safe; the terminal's gate guards it.
/// </summary>
internal sealed class ScrollbackRing(int capacity)
{
    private readonly Queue<string> _chunks = new();
    private int _length;

    public int Length => _length;

    public void Append(string chunk)
    {
        if (chunk.Length == 0) return;
        if (chunk.Length >= capacity)
        {
            // One chunk bigger than the whole ring: keep its tail, from a line start so the cut is less likely to land in a sequence.
            var tail = chunk[^capacity..];
            var newline = tail.IndexOf('\n');
            _chunks.Clear();
            _chunks.Enqueue(newline >= 0 ? tail[(newline + 1)..] : tail);
            _length = _chunks.Peek().Length;
            return;
        }
        _chunks.Enqueue(chunk);
        _length += chunk.Length;
        while (_length > capacity) _length -= _chunks.Dequeue().Length;
    }

    public string Text()
    {
        var sb = new StringBuilder(_length);
        foreach (var c in _chunks) sb.Append(c);
        return sb.ToString();
    }

    public void Clear()
    {
        _chunks.Clear();
        _length = 0;
    }
}
