using System.Text;

namespace Spike.Core.Pty;

/// <summary>
/// Forwards terminal output either per read or coalesced on a timer. The spike compares the two to see
/// whether per-read postMessage can keep up with heavy output.
/// </summary>
public sealed class OutputPump : IDisposable
{
    private readonly Action<string> _emit;
    private readonly Lock _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly Timer _timer;
    private bool _scheduled;

    public OutputPump(Action<string> emit)
    {
        _emit = emit;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public int CoalesceMs { get; set; }

    public void Push(string chunk)
    {
        var delay = CoalesceMs;
        if (delay <= 0)
        {
            _emit(chunk);
            return;
        }
        lock (_gate)
        {
            _pending.Append(chunk);
            if (_scheduled) return;
            _scheduled = true;
            _timer.Change(delay, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        string text;
        lock (_gate)
        {
            text = _pending.ToString();
            _pending.Clear();
            _scheduled = false;
        }
        if (text.Length > 0) _emit(text);
    }

    public void Dispose() => _timer.Dispose();
}
