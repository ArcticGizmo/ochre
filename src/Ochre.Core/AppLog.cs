using System.Collections.Concurrent;
using System.Text;

namespace Ochre.Core;

/// <summary>
/// A small daily file log. Callers must never pass tokens or message bodies (security.md threat 6); log what
/// happened, not what was said. Writes are queued so logging never blocks the UI thread.
/// </summary>
public sealed class AppLog : IDisposable
{
    private readonly string _directory;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 10_000);
    private readonly Thread _writer;

    public AppLog(string directory)
    {
        _directory = directory;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "ochre-log" };
        _writer.Start();
    }

    public void Info(string message) => Enqueue("INF", message);

    public void Warn(string message) => Enqueue("WRN", message);

    public void Error(string message, Exception? ex = null) =>
        Enqueue("ERR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void Enqueue(string level, string message)
    {
        // Dropping a line beats stalling the caller if the disk is wedged.
        _queue.TryAdd($"{DateTimeOffset.Now:O} {level} {message}");
    }

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, $"ochre-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }
}
