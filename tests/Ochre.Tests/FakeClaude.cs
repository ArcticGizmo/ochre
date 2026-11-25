using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Ochre.Core.Claude.Control;

namespace Ochre.Tests;

/// <summary>Records every launch and hands back a <see cref="FakeClaudeProcess"/> the test drives.</summary>
internal sealed class FakeClaudeLauncher : IClaudeLauncher
{
    private readonly List<(ClaudeLaunch Launch, FakeClaudeProcess Process)> _launches = [];

    public IReadOnlyList<(ClaudeLaunch Launch, FakeClaudeProcess Process)> Launches
    {
        get
        {
            lock (_launches) return [.. _launches];
        }
    }

    public FakeClaudeProcess Last => Launches[^1].Process;

    public IClaudeProcess Start(ClaudeLaunch launch)
    {
        var process = new FakeClaudeProcess();
        lock (_launches) _launches.Add((launch, process));
        return process;
    }
}

/// <summary>
/// A <c>claude</c> that exists only in memory: the test writes the stream-json lines it would print, and reads the
/// lines the controller sent it. Closing its input makes it exit cleanly, as the real CLI does.
/// </summary>
internal sealed class FakeClaudeProcess : IClaudeProcess
{
    private readonly BlockingCollection<string> _out = new();
    private readonly RecordingWriter _in;
    private readonly ManualResetEventSlim _exited = new();

    public FakeClaudeProcess()
    {
        _in = new RecordingWriter(() => Exit(0));
        Output = new QueueReader(_out);
    }

    public TextReader Output { get; }
    public TextWriter Input => _in;
    public Task<string> Errors { get; } = Task.FromResult("");
    public bool HasExited => _exited.IsSet;
    public int ExitCode { get; private set; }

    /// <summary>The JSON lines the controller wrote to the CLI, parsed.</summary>
    public IReadOnlyList<JsonNode> Sent => _in.Lines.Select(l => JsonNode.Parse(l)!).ToList();

    /// <summary>The text of every user message sent.</summary>
    public IReadOnlyList<string> Prompts => Sent
        .Where(n => (string?)n["type"] == "user")
        .Select(n => (string?)n["message"]?["content"]?[0]?["text"] ?? "")
        .ToList();

    public void Emit(string jsonLine) => _out.Add(jsonLine);

    public void Exit(int code)
    {
        if (_exited.IsSet) return;
        ExitCode = code;
        _exited.Set();
        _out.CompleteAdding();
    }

    public bool WaitForExit(TimeSpan timeout) => _exited.Wait(timeout);
    public void Kill() => Exit(-1);
    public void Dispose() { }

    private sealed class QueueReader(BlockingCollection<string> lines) : TextReader
    {
        public override string? ReadLine()
        {
            try
            {
                return lines.Take();
            }
            catch (InvalidOperationException)
            {
                return null;   // completed: the process exited
            }
        }
    }

    private sealed class RecordingWriter(Action onClose) : TextWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();
        public IReadOnlyList<string> Lines => [.. _lines];
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => _lines.Enqueue(value ?? "");

        protected override void Dispose(bool disposing)
        {
            if (disposing) onClose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>stream-json records in the shapes the CLI prints them.</summary>
internal static class StreamJson
{
    public static string Init(string sessionId, string mode = "default") =>
        $$"""{"type":"system","subtype":"init","session_id":"{{sessionId}}","model":"claude-test","permissionMode":"{{mode}}","tools":[]}""";

    public static string Text(string text) =>
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":""" + Quote(text) + """}],"usage":{"input_tokens":10}}}""";

    public static string Result() =>
        """{"type":"result","subtype":"success","is_error":false,"total_cost_usd":0.01,"duration_ms":5,"usage":{"input_tokens":10,"output_tokens":2}}""";

    public static string AskPermission(string requestId, string command) =>
        """{"type":"control_request","request_id":""" + Quote(requestId) +
        ""","request":{"subtype":"can_use_tool","tool_name":"Bash","description":"run it","input":{"command":""" + Quote(command) + """}}}""";

    private static string Quote(string s) => System.Text.Json.JsonSerializer.Serialize(s, Ochre.Tests.TestJson.Default.String);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class TestJson : System.Text.Json.Serialization.JsonSerializerContext;
