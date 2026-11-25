using System.Diagnostics;
using System.Text;
using Spike.Core.Bridge;
using Spike.Core.Pty;

namespace Spike.Core;

/// <summary>
/// Host-lifetime state shared by every shell: the terminal outlives windows, as it will in Ochre.
/// Each shell only supplies a tray, a window and a way to post strings to the page.
/// </summary>
public sealed class SpikeHost : IDisposable
{
    private const long MaxReadBytes = 5 * 1024 * 1024;

    private readonly string _shellName;
    private readonly Lock _gate = new();
    private readonly OutputPump _pump;
    private PseudoConsoleSession? _pty;
    private BridgeDispatcher? _activeBridge;
    private double _coldStartMs;
    private long _windowOpenRequestedTicks;
    private double? _lastWindowOpenMs;

    public SpikeHost(string shellName)
    {
        _shellName = shellName;
        _pump = new OutputPump(text => ActiveBridge?.PostEvent("pty.output", new PtyOutput(text), BridgeJson.Default.PtyOutput));
        Directory.CreateDirectory(DataDir);
    }

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ochre-spike");

    private BridgeDispatcher? ActiveBridge
    {
        get { lock (_gate) return _activeBridge; }
    }

    public void MarkTrayReady()
    {
        _coldStartMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        Log($"cold-start-to-tray {_coldStartMs:F0} ms");
    }

    public void MarkWindowOpenRequested() => _windowOpenRequestedTicks = Stopwatch.GetTimestamp();

    /// <summary>Creates the bridge for a newly opened window; events go to the most recent window.</summary>
    public BridgeDispatcher AttachWindow(Action<string> post)
    {
        var bridge = new BridgeDispatcher(post);
        bridge.UnexpectedError += (type, ex) => Log($"bridge {type} failed: {ex.GetType().Name}: {ex.Message}");
        Register(bridge);
        lock (_gate) _activeBridge = bridge;
        return bridge;
    }

    public void DetachWindow(BridgeDispatcher bridge)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeBridge, bridge)) _activeBridge = null;
        }
    }

    private void Register(BridgeDispatcher b)
    {
        var j = BridgeJson.Default;
        b.Handle("app.ready", j.Empty, j.Ack, _ =>
        {
            if (_windowOpenRequestedTicks != 0)
            {
                _lastWindowOpenMs = Stopwatch.GetElapsedTime(_windowOpenRequestedTicks).TotalMilliseconds;
                _windowOpenRequestedTicks = 0;
                Log($"window-open-to-ui-ready {_lastWindowOpenMs:F0} ms");
            }
            return new Ack();
        });
        b.Handle("metrics.get", j.Empty, j.Metrics, _ =>
        {
            using var self = Process.GetCurrentProcess();
            return new Metrics(_shellName, _coldStartMs, _lastWindowOpenMs, self.WorkingSet64 / 1024.0 / 1024.0);
        });
        b.Handle("bench.echo", j.EchoMessage, j.EchoMessage, m => m);
        b.Handle("bench.report", j.BenchReport, j.Ack, r =>
        {
            Log("bench " + (r.Line.Length > 2000 ? r.Line[..2000] : r.Line).ReplaceLineEndings(" "));
            return new Ack();
        });
        b.Handle("bench.makeBigFile", j.Empty, j.BigFileResult, _ => new ValueTask<BigFileResult>(MakeBigFileAsync()));
        b.Handle("tasks.list", j.Empty, j.TaskSummaryArray, _ => SampleTasks.Load());
        b.Handle("fs.read", j.ReadFileRequest, j.ReadFileResult, r => new ValueTask<ReadFileResult>(ReadFileAsync(r)));
        b.Handle("pty.start", j.PtyStartRequest, j.PtyStartResult, StartPty);
        b.Handle("pty.input", j.PtyInput, j.Ack, i =>
        {
            CurrentPty().Write(i.Data);
            return new Ack();
        });
        b.Handle("pty.resize", j.PtyResize, j.Ack, r =>
        {
            CurrentPty().Resize(ClampCols(r.Cols), ClampRows(r.Rows));
            return new Ack();
        });
        b.Handle("pty.setMode", j.PtyMode, j.Ack, m =>
        {
            _pump.CoalesceMs = Math.Clamp(m.CoalesceMs, 0, 200);
            return new Ack();
        });
    }

    private PtyStartResult StartPty(PtyStartRequest r)
    {
        lock (_gate)
        {
            if (_pty is not null)
            {
                _pty.Resize(ClampCols(r.Cols), ClampRows(r.Rows));
                return new PtyStartResult(true, _pty.ProcessId);
            }
            var pty = PseudoConsoleSession.Start(ResolvePowerShell(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ClampCols(r.Cols), ClampRows(r.Rows));
            pty.Output += _pump.Push;
            pty.Exited += code =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pty, pty)) _pty = null;
                }
                ActiveBridge?.PostEvent("pty.exit", new PtyExit(code), BridgeJson.Default.PtyExit);
                pty.Dispose();
            };
            _pty = pty;
            return new PtyStartResult(false, pty.ProcessId);
        }
    }

    private PseudoConsoleSession CurrentPty()
    {
        lock (_gate) return _pty ?? throw new BridgeRejectException("no terminal running");
    }

    private static short ClampCols(int cols) => (short)Math.Clamp(cols, 2, 1000);
    private static short ClampRows(int rows) => (short)Math.Clamp(rows, 2, 500);

    private static string ResolvePowerShell()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "pwsh.exe");
            if (File.Exists(candidate)) return $"\"{candidate}\" -NoLogo";
        }
        return "powershell.exe -NoLogo";
    }

    private static async Task<ReadFileResult> ReadFileAsync(ReadFileRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Path) || !Path.IsPathFullyQualified(r.Path))
            throw new BridgeRejectException("path must be absolute");
        var info = new FileInfo(r.Path);
        if (!info.Exists) throw new BridgeRejectException("file not found");
        if (info.Length > MaxReadBytes) throw new BridgeRejectException("file larger than 5 MB");
        await using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        return new ReadFileResult(info.FullName, content, LanguageFor(info.Extension));
    }

    private static string LanguageFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".ts" or ".tsx" => "typescript",
        ".js" or ".jsx" or ".mjs" => "javascript",
        ".json" => "json",
        ".md" => "markdown",
        ".ps1" => "powershell",
        ".xml" or ".csproj" or ".props" => "xml",
        ".css" => "css",
        ".html" => "html",
        ".yml" or ".yaml" => "yaml",
        _ => "plaintext",
    };

    private static async Task<BigFileResult> MakeBigFileAsync()
    {
        var path = Path.Combine(DataDir, "big-50mb.txt");
        const long target = 50L * 1024 * 1024;
        if (!File.Exists(path) || new FileInfo(path).Length < target)
        {
            await using var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
            long written = 0;
            for (var i = 0; written < target; i++)
            {
                var line = $"{i:D8} The quick brown fox jumps over the lazy dog. 0123456789 abcdefghijklmnopqrstuvwxyz";
                await w.WriteLineAsync(line).ConfigureAwait(false);
                written += line.Length + 2;
            }
        }
        return new BigFileResult(path, new FileInfo(path).Length);
    }

    public void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(DataDir, "timing.log"), $"{DateTime.Now:O} [{_shellName}] {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        PseudoConsoleSession? pty;
        lock (_gate)
        {
            pty = _pty;
            _pty = null;
        }
        pty?.Dispose();
        _pump.Dispose();
    }
}
