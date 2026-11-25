using System.Diagnostics;

namespace Ochre.Core.Claude.Control;

/// <summary>What the controller asks to run: <c>claude</c> with these (already safe) arguments, in this folder,
/// with these environment changes (a null value removes the variable).</summary>
internal sealed record ClaudeLaunch(string Args, string Cwd, IReadOnlyDictionary<string, string?> Environment);

/// <summary>A running <c>claude</c> as the controller sees it: lines out, lines in, an exit.</summary>
internal interface IClaudeProcess : IDisposable
{
    TextReader Output { get; }
    TextWriter Input { get; }
    Task<string> Errors { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    bool WaitForExit(TimeSpan timeout);
    void Kill();
}

/// <summary>Starts <c>claude</c>. The seam lets tests drive a session from recorded stream-json without a process.</summary>
internal interface IClaudeLauncher
{
    IClaudeProcess Start(ClaudeLaunch launch);
}

/// <summary>The real launcher: resolves <c>claude</c> from PATH (never the working folder) and pipes its streams.</summary>
internal sealed class SystemClaudeLauncher : IClaudeLauncher
{
    public static readonly SystemClaudeLauncher Instance = new();

    public IClaudeProcess Start(ClaudeLaunch launch)
    {
        // Resolved to an absolute path, never against the cwd: the session runs inside a repo Ochre didn't write, and
        // a `claude.cmd` committed there would otherwise run in place of the real CLI.
        var psi = ClaudeCli.CreateStartInfo(launch.Args);
        psi.WorkingDirectory = launch.Cwd;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        psi.StandardInputEncoding = new System.Text.UTF8Encoding(false);
        foreach (var (key, value) in launch.Environment)
        {
            if (value is null) psi.Environment.Remove(key);
            else psi.Environment[key] = value;
        }
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start claude.");
        return new SystemClaudeProcess(process);
    }

    private sealed class SystemClaudeProcess : IClaudeProcess
    {
        private readonly Process _process;

        public SystemClaudeProcess(Process process)
        {
            _process = process;
            // Drained from the start so a chatty stderr can't back-pressure the child into a deadlock.
            Errors = process.StandardError.ReadToEndAsync();
        }

        public TextReader Output => _process.StandardOutput;
        public TextWriter Input => _process.StandardInput;
        public Task<string> Errors { get; }
        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;
        public bool WaitForExit(TimeSpan timeout) => _process.WaitForExit(timeout);
        public void Kill() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();
    }
}
