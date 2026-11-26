using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ochre.Core.Claude;

namespace Ochre.Modules.Sprig;

/// <summary>What a sprig command said: <see cref="Ok"/>, or the error <see cref="Code"/> sprig's <c>--json</c> gives
/// (<c>untrusted</c>, <c>inputs-needed</c>, <c>docker-unavailable</c> ...) and its message.</summary>
internal sealed record SprigActionResult(bool Ok, string? Code = null, string? Error = null)
{
    /// <summary>Sprig couldn't be found or started.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The command didn't return in time and was stopped.</summary>
    public const string TimedOut = "timed-out";

    /// <summary>Sprig needs a person in its own UI to go on: approving a definition, giving inputs, taking over a
    /// checkout, or fixing a slot.</summary>
    public bool NeedsSprig => Code is "untrusted" or "inputs-needed" or "held" or "slot-broken" or "problems";

    /// <summary>Sprig's message with its code, as the user should read it.</summary>
    public string Message => Code is null or "error" ? Error ?? "sprig failed" : $"{Error} ({Code})";
}

/// <summary>What Ochre can ask sprig to do to a workspace, or one of its apps.</summary>
internal enum SprigAction
{
    Start,
    Restart,
    Stop,
    Pause,
    Resume,
}

internal static class SprigActions
{
    /// <summary>What a workspace is doing while <paramref name="action"/> runs, so progress can show from the click
    /// rather than from sprig's first report.</summary>
    public static SprigActivity ActivityOf(SprigAction action)
        => action is SprigAction.Stop or SprigAction.Pause ? SprigActivity.Stopping : SprigActivity.Starting;
}

/// <summary>A slot <c>sprig checkout</c> made or reused for a branch.</summary>
/// <param name="Key">The workspace, <c>orchestration/name</c>.</param>
/// <param name="Folder">Where the task's repo is checked out in it.</param>
internal sealed record SprigCheckout(string Key, string Folder, string? Branch, bool BranchCreated);

/// <summary>
/// Finds sprig and runs its CLI for the things Ochre asks it to <em>do</em>. Everything Ochre reads about running
/// workspaces comes from the <c>sprig watch --json</c> stream (see <see cref="SprigStatusFold"/>); only actions launch
/// a command.
/// </summary>
/// <remarks>
/// <para>The CLI rather than sprig's host pipe, because the pipe is internal to sprig and free to change, while
/// <c>--json</c> is its stable contract. Commands that can start apps pass <c>--no-wait</c>: progress shows up on
/// the stream, so there's nothing to wait for here.</para>
/// <para>Every child gets redirected, UTF-8 stdio and a closed stdin, so sprig sees no terminal and never prompts.
/// Blocking; call off the UI thread.</para>
/// </remarks>
internal static class SprigCli
{
    // Velopack installs sprig under its packId; `current` holds sprig.exe beside sprig-gui and sprig-host.
    private static string InstalledPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sprig", "current", "sprig.exe");

    /// <summary>
    /// The sprig executable, or null when it isn't installed. The install location first, then PATH. On Windows
    /// it asks for <c>sprig.exe</c> by name, because sprig's <c>bin</c> folder on PATH holds <c>sprig.cmd</c> and
    /// <c>sprig.ps1</c> shims (for <c>sprig cd</c>), which aren't the CLI.
    /// </summary>
    public static string? Locate()
    {
        if (OperatingSystem.IsWindows() && File.Exists(InstalledPath)) return InstalledPath;
        return ExecutableResolver.Find(OperatingSystem.IsWindows() ? "sprig.exe" : "sprig");
    }

    /// <summary>How to start the long-lived <c>sprig watch --json</c>. Stdin stays redirected <em>and open</em>:
    /// sprig ends the watch when it closes, which is how it learns Ochre has gone.</summary>
    public static ProcessStartInfo WatchStartInfo(string exe)
    {
        var psi = BaseStartInfo(exe, null);
        psi.ArgumentList.Add("watch");
        psi.ArgumentList.Add("--json");
        return psi;
    }

    /// <summary>The command line for <paramref name="action"/> on a workspace, or on one app of it.</summary>
    internal static IReadOnlyList<string> ActionArgs(SprigAction action, SprigWorkspace workspace, string? app)
    {
        var verb = action switch
        {
            SprigAction.Start => "start",
            SprigAction.Restart => "restart",
            SprigAction.Stop => "stop",
            SprigAction.Pause => "pause",
            _ => "resume",
        };
        var args = new List<string> { verb, workspace.Orchestration, workspace.Name };
        if (app is not null && action is SprigAction.Start or SprigAction.Restart or SprigAction.Stop) args.Add(app);
        args.Add("--json");
        if (action is SprigAction.Start or SprigAction.Restart or SprigAction.Resume) args.Add("--no-wait");
        return args;
    }

    /// <summary>Runs <paramref name="action"/>. A start can first have to make the workspace ready (worktrees,
    /// files, its infra), which can take minutes the first time an image is pulled, so the limit is generous.</summary>
    public static SprigActionResult Run(SprigAction action, SprigWorkspace workspace, string? app = null)
        => Run(ActionArgs(action, workspace, app), TimeSpan.FromMinutes(10), null).Result;

    /// <summary>
    /// <c>sprig checkout -b &lt;branch&gt;</c> in <paramref name="repo"/>: the branch in the slot it's in already, else
    /// the lowest free slot, else a new one. Nothing starts. Never passes <c>--trust</c>: approving a definition is
    /// the user's call, made in sprig.
    /// </summary>
    public static (SprigActionResult Result, SprigCheckout? Checkout) Checkout(string repo, string branch)
    {
        var (result, stdout) = Run(["checkout", "-b", branch, "--json"], TimeSpan.FromMinutes(10), repo);
        if (!result.Ok) return (result, ParseCheckout(stdout, repo));
        return ParseCheckout(stdout, repo) is { } checkout
            ? (result, checkout)
            : (new SprigActionResult(false, "error", "sprig made the slot but its answer didn't say where"), null);
    }

    /// <summary><c>sprig free</c>: stops the slot's apps and infra and detaches its worktrees, keeping its branch, data,
    /// ports and values. Sprig refuses while a worktree has uncommitted changes.</summary>
    public static SprigActionResult Free(string orchestration, string name)
        => Run(["free", orchestration, name, "--json"], TimeSpan.FromMinutes(5), null).Result;

    /// <summary><c>sprig info</c> in <paramref name="repo"/>: whether sprig knows the folder at all (it must be on
    /// sprig's Repos list before a slot can be made for it).</summary>
    public static SprigActionResult Info(string repo)
        => Run(["info", "--json"], TimeSpan.FromSeconds(30), repo).Result;

    /// <summary>Brings sprig's window up on a workspace, launching the app if it isn't running.</summary>
    public static SprigActionResult Open(SprigWorkspace workspace)
        => Run(["open", workspace.Orchestration, workspace.Name, "--json"], TimeSpan.FromSeconds(30), null).Result;

    /// <param name="cwd">Where to run, for a command that reads "the repo this folder is"; else sprig's own folder.</param>
    private static (SprigActionResult Result, string Stdout) Run(IReadOnlyList<string> args, TimeSpan limit, string? cwd)
    {
        if (Locate() is not { } exe) return (new(false, SprigActionResult.Unavailable, "sprig isn't installed"), "");
        try
        {
            var psi = BaseStartInfo(exe, cwd);
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (new(false, SprigActionResult.Unavailable, "sprig didn't start"), "");
            p.StandardInput.Close();
            // Both pipes are read before waiting: a full pipe nobody drains would block sprig and turn a long
            // message into a hang.
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(limit))
            {
                // Only the CLI itself. A start may have launched sprig's host, which is this process's child by
                // parent id; killing the tree would take the host and every app it runs down with it.
                try
                {
                    p.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
                return (new(false, SprigActionResult.TimedOut, "sprig didn't finish in time"), "");
            }
            // Bounded, so a process that kept the pipes open after sprig exited can't hold this thread; the exit
            // code still decides.
            var drained = Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(5));
            var output = drained ? stdout.Result : "";
            return (Parse(p.ExitCode, output, drained ? stderr.Result : ""), output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (new(false, SprigActionResult.Unavailable, e.Message), "");
        }
    }

    /// <summary>Reads a command's outcome. The exit code decides; the last JSON object on stdout carries the
    /// <c>code</c> and <c>error</c> (progress may come first as other lines).</summary>
    internal static SprigActionResult Parse(int exitCode, string stdout, string stderr)
    {
        string? code = null, error = null;
        foreach (var root in JsonObjects(stdout))
        {
            if (!root.TryGetProperty("ok", out _)) continue;
            code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            error = root.TryGetProperty("error", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            break;
        }
        if (exitCode == 0) return new(true);
        if (error is null && stderr.Trim() is { Length: > 0 } text)
            error = text.StartsWith("error: ", StringComparison.Ordinal) ? text["error: ".Length..] : text;
        return new(false, code ?? "error", error ?? $"sprig exited with code {exitCode}");
    }

    /// <summary>
    /// The slot in <c>checkout --json</c>'s answer (<c>{ ok, workspace: { Key, Folder, Branch, Repos: [{ Source, Dir,
    /// BranchCreated }] } }</c>): where <paramref name="repo"/> is checked out in it, falling back to the slot's own
    /// folder for a one-repo slot. Pure.
    /// </summary>
    internal static SprigCheckout? ParseCheckout(string stdout, string repo)
    {
        foreach (var root in JsonObjects(stdout))
        {
            if (!root.TryGetProperty("workspace", out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (Str(w, "Key") is not { } key) return null;
            string? dir = null;
            var created = false;
            if (w.TryGetProperty("Repos", out var repos) && repos.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in repos.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object || !SamePath(Str(r, "Source"), repo)) continue;
                    dir = Str(r, "Dir");
                    created = r.TryGetProperty("BranchCreated", out var b) && b.ValueKind == JsonValueKind.True;
                }
            }
            var folder = dir ?? Str(w, "Folder");
            return folder is null ? null : new SprigCheckout(key, folder, Str(w, "Branch"), created);
        }
        return null;
    }

    // The JSON objects on stdout, last first: the answer is the last one, after any progress lines. Sprig pretty-prints
    // its answer across lines, so each object is read from a line that opens one to that line's own end, or failing
    // that to the end of the output.
    private static IEnumerable<JsonElement> JsonObjects(string stdout)
    {
        var lines = stdout.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            // Only an unindented brace opens a top-level object; indented ones are inside one.
            if (!lines[i].StartsWith('{')) continue;
            if (TryObject(lines[i]) is { } single)
            {
                yield return single;
                continue;
            }
            if (TryObject(string.Join('\n', lines[i..])) is { } spanning) yield return spanning;
        }
    }

    private static JsonElement? TryObject(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool SamePath(string? a, string b) =>
        a is not null && string.Equals(SprigFolders.Normalize(a), SprigFolders.Normalize(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static ProcessStartInfo BaseStartInfo(string exe, string? cwd) => new(exe)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        // Sprig reads "the workspace this folder is in" from the working directory. Commands that name their
        // workspace run from sprig's own folder rather than whatever Ochre's happens to be.
        WorkingDirectory = cwd ?? Path.GetDirectoryName(exe) ?? "",
    };
}
