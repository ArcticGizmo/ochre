using System.Text.Json.Nodes;

namespace Ochre.Core.Claude.Control;

/// <summary>
/// Spawns and drives a Claude Code session over the CLI's bidirectional stream-json interface
/// (<c>claude -p --input-format stream-json --output-format stream-json --permission-prompt-tool stdio</c>)
/// — the same wire protocol the official Agent SDKs wrap. The host owns the process: it sends user prompts,
/// answers permission requests, switches permission mode and interrupts, and decodes the output stream
/// into <see cref="SessionEvent"/>s via <see cref="StreamJsonParser"/>.
///
/// <para>Threading: <see cref="EventReceived"/> and <see cref="Exited"/> are raised on the background
/// pump thread. Writers are serialised by a lock.</para>
/// </summary>
internal sealed class ClaudeSessionController(IClaudeLauncher? launcher = null) : IDisposable
{
    /// <summary>A decoded output event. Raised on the pump thread.</summary>
    public event Action<SessionEvent>? EventReceived;

    /// <summary>The process ended: exit code + a clipped stderr tail (empty when clean).</summary>
    public event Action<int, string>? Exited;

    private readonly IClaudeLauncher _launcher = launcher ?? SystemClaudeLauncher.Instance;
    private IClaudeProcess? _process;
    private TextWriter? _stdin;
    private readonly Lock _writeLock = new();
    private int _requestCounter;

    /// <summary>The session id. Known from launch (pinned via <c>--session-id</c> for a new session, or the
    /// <c>--resume</c> id), then confirmed by <c>system/init</c>.</summary>
    public string? SessionId { get; private set; }
    public string Cwd { get; private set; } = "";
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Launch diagnostics (paths and ids only, never prompts or output).</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Launches the session in <paramref name="options"/>' working directory. Model, mode and effort are
    /// validated against known tokens since they end up on a command line. A resume continues the conversation
    /// under the <em>same</em> id and transcript; otherwise a fresh GUID (or the caller's) is pinned via
    /// <c>--session-id</c>, so the id exists before the CLI's <c>init</c>.</summary>
    public void Start(SessionStartOptions options)
    {
        if (IsRunning) throw new InvalidOperationException("Session already running.");

        bool resume = IsSafeToken(options.ResumeSessionId);
        bool fork = options.Fork && resume;
        var id = resume && !fork ? options.ResumeSessionId!
            : IsSafeToken(options.NewSessionId) ? options.NewSessionId! : Guid.NewGuid().ToString();

        var args = "-p --input-format stream-json --output-format stream-json --verbose" +
                   " --include-partial-messages --permission-prompt-tool stdio";
        args += fork ? $" --resume {options.ResumeSessionId} --fork-session --session-id {id}"
              : resume ? $" --resume {id}"
              : $" --session-id {id}";
        if (resume && IsSafeToken(options.ResumeAt)) args += $" --resume-session-at {options.ResumeAt}";
        if (IsSafeToken(options.Model)) args += $" --model {options.Model}";
        if (IsSafeToken(options.PermissionMode) && options.PermissionMode != "default") args += $" --permission-mode {options.PermissionMode}";
        if (IsEffortLevel(options.Effort)) args += $" --effort {options.Effort}";
        if (options.AllowedTools.Count > 0)
        {
            // Permission rules such as `Bash(ochre task *)`: a closed character set, so each rides as one quoted argument.
            if (!options.AllowedTools.All(IsSafeToolRule)) throw new ArgumentException("An allowed-tool rule has characters that can't be passed safely.", nameof(options));
            args += " --allowedTools " + string.Join(" ", options.AllowedTools.Select(ClaudeCli.QuotePrompt));
        }
        if (options.AppendSystemPrompt is { } note)
        {
            // It rides the command line, so only text no shell would act on inside quotes is allowed through.
            if (!ClaudeCli.IsSafePrompt(note)) throw new ArgumentException("The system prompt note has characters that can't be passed safely.", nameof(options));
            args += $" --append-system-prompt {ClaudeCli.QuotePrompt(note)}";
        }

        var env = new Dictionary<string, string?>
        {
            // Have the CLI report its own turn state (system/session_state_changed). Undocumented; an older CLI
            // ignores it and the conversation model infers the state from results instead.
            ["CLAUDE_CODE_EMIT_SESSION_STATE_EVENTS"] = "1",
            // Print mode records no file checkpoints unless asked, and without them `rewind_files` has nothing to restore.
            ["CLAUDE_CODE_ENABLE_SDK_FILE_CHECKPOINTING"] = "1",
        };
        // Set on the child env, never the command line, so an odd path can't break arg parsing or inject.
        if (!string.IsNullOrWhiteSpace(options.ConfigDir)) env["CLAUDE_CONFIG_DIR"] = options.ConfigDir;
        foreach (var (key, value) in options.Environment) env[key] = value;
        Log?.Invoke($"controlled session start: cwd={options.Cwd} resume={resume} id={id}");

        // Registered before the process exists so nothing can race the pre-init window; a failed launch
        // releases it again below.
        SessionId = id;
        Cwd = options.Cwd;
        ControlledSessions.Register(id);

        IClaudeProcess process;
        try
        {
            process = _launcher.Start(new ClaudeLaunch(args, options.Cwd, env));
        }
        catch
        {
            ControlledSessions.Unregister(id);
            SessionId = null;
            throw;
        }
        _process = process;
        _stdin = process.Input;

        // The control-protocol handshake; the CLI answers with its capability catalogue (ignored here).
        WriteLine(new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = NextRequestId(),
            ["request"] = new JsonObject { ["subtype"] = "initialize" },
        });

        Task.Run(() => Pump(process));
    }

    private void Pump(IClaudeProcess process)
    {
        var stderr = process.Errors;
        try
        {
            while (process.Output.ReadLine() is { } line)
            {
                foreach (var ev in StreamJsonParser.Parse(line))
                {
                    // Normally confirms the pinned/resumed id; if the CLI reports a different one (`/clear` does),
                    // follow it so ownership tracks the transcript that is actually being written.
                    if (ev is SessionInitEvent init && init.SessionId.Length > 0 && init.SessionId != SessionId)
                    {
                        ControlledSessions.Unregister(SessionId);
                        SessionId = init.SessionId;
                        ControlledSessions.Register(init.SessionId);
                    }
                    EventReceived?.Invoke(ev);
                }
            }
        }
        catch
        {
            // Stream torn down mid-read (Stop/Dispose); fall through to the exit notification.
        }

        int exitCode = -1;
        string errTail = "";
        try
        {
            process.WaitForExit(TimeSpan.FromSeconds(5));
            exitCode = process.HasExited ? process.ExitCode : -1;
            var err = stderr.IsCompletedSuccessfully ? stderr.Result : "";
            errTail = ToolSummary.Clip(err.Length > 400 ? err[^400..] : err);
        }
        catch { /* best effort */ }
        ControlledSessions.Unregister(SessionId);
        Exited?.Invoke(exitCode, errTail);
    }

    /// <summary>Queues a user prompt; the CLI runs a full agentic turn per prompt.</summary>
    public void SendPrompt(string text) => SendPrompt(text, null);

    /// <summary>Queues a user prompt, optionally with image attachments. The wire message's <c>content</c> is
    /// a block array (the Anthropic Messages shape the stream-json input consumes): a <c>text</c> block plus
    /// one <c>image</c> block per attachment (base64).</summary>
    public void SendPrompt(string text, IReadOnlyList<ImageContent>? images)
    {
        var content = new JsonArray();
        if (!string.IsNullOrEmpty(text))
            content.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = text });
        if (images is not null)
            foreach (var img in images)
                content.Add((JsonNode)new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64", ["media_type"] = img.MediaType, ["data"] = img.Base64,
                    },
                });
        if (content.Count == 0) return;   // nothing to send
        WriteLine(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = content },
        });
    }

    /// <summary>Answers a <see cref="PermissionRequestEvent"/>. On allow the tool's input is echoed back
    /// as <c>updatedInput</c> — unchanged, or <paramref name="updatedInput"/> when the caller edited it (how
    /// <c>AskUserQuestion</c> is answered: the input plus an <c>answers</c> object, see
    /// <see cref="AskUserQuestionInput"/>).</summary>
    public void RespondToPermission(PermissionRequestEvent request, bool allow, string? denyMessage = null, JsonNode? updatedInput = null)
    {
        JsonObject verdict;
        if (allow)
        {
            JsonNode? input = updatedInput;
            if (input is null)
                try { input = JsonNode.Parse(request.InputJson); } catch { /* omit on parse failure */ }
            verdict = new JsonObject { ["behavior"] = "allow", ["updatedInput"] = input ?? new JsonObject() };
        }
        else
        {
            verdict = new JsonObject { ["behavior"] = "deny", ["message"] = denyMessage ?? "Denied by the user in Ochre." };
        }
        WriteLine(new JsonObject
        {
            ["type"] = "control_response",
            ["response"] = new JsonObject
            {
                ["subtype"] = "success",
                ["request_id"] = request.RequestId,
                ["response"] = verdict,
            },
        });
    }

    /// <summary>Switches the live session's permission mode; acked as a <see cref="ModeChangedEvent"/>.</summary>
    public void SetPermissionMode(string mode)
    {
        if (!IsSafeToken(mode)) return;
        WriteLine(new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = NextRequestId(),
            ["request"] = new JsonObject { ["subtype"] = "set_permission_mode", ["mode"] = mode },
        });
    }

    /// <summary>The request id stamped on the <c>remote_control</c> control request, so the ack is recognised in
    /// the output stream (<see cref="StreamJsonParser"/>) rather than mistaken for another control ack. Only one is
    /// ever in flight.</summary>
    public const string RemoteControlRequestId = "ochre-rc";

    /// <summary>Enables or disables Remote Control for the live session via the CLI's <c>remote_control</c>
    /// control request. The enable ack carries the claude.ai session URL + bridge id, decoded into a
    /// <see cref="RemoteControlEvent"/>; disable acks empty.</summary>
    public void RequestRemoteControl(bool enabled) => WriteLine(new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = RemoteControlRequestId,
        ["request"] = new JsonObject { ["subtype"] = "remote_control", ["enabled"] = enabled },
    });

    /// <summary>The prefix of the request id on a <c>rewind_files</c> request, so its ack is recognised
    /// (<see cref="StreamJsonParser"/>) and matched back to the caller by the full id.</summary>
    public const string RewindFilesRequestPrefix = "ochre-rewind-";

    /// <summary>Restores the files the file tools changed since <paramref name="userMessageId"/> (a transcript prompt
    /// uuid) to how they stood before it: the Agent SDK's <c>rewind_files</c> control request. A
    /// <paramref name="dryRun"/> only reports what would change. Returns the request id the ack will carry, or null
    /// when the id isn't a safe token (nothing is sent).</summary>
    public string? RewindFiles(string userMessageId, bool dryRun)
    {
        if (!IsSafeToken(userMessageId)) return null;
        var requestId = RewindFilesRequestPrefix + Interlocked.Increment(ref _requestCounter);
        var request = new JsonObject { ["subtype"] = "rewind_files", ["user_message_id"] = userMessageId };
        if (dryRun) request["dry_run"] = true;
        WriteLine(new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = requestId,
            ["request"] = request,
        });
        return requestId;
    }

    /// <summary>Switches the model mid-session — the Agent SDK's <c>setModel</c> control request.</summary>
    public void SetModel(string model)
    {
        if (!IsSafeToken(model)) return;
        WriteLine(new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = NextRequestId(),
            ["request"] = new JsonObject { ["subtype"] = "set_model", ["model"] = model },
        });
    }

    /// <summary>Changes the effort level mid-session by sending the documented <c>/effort &lt;level&gt;</c> command
    /// as a user message (slash commands are accepted over stream-json input in print mode).</summary>
    public void SetEffort(string level)
    {
        if (!IsEffortLevel(level) && level != "auto") return;   // "auto" clears the saved level
        SendPrompt($"/effort {level}");
    }

    /// <summary>The CLI's accepted effort levels (<c>--effort</c> / <c>/effort</c>).</summary>
    public static readonly string[] EffortLevels = ["low", "medium", "high", "xhigh", "max"];

    public static bool IsEffortLevel(string? s) => s is not null && Array.IndexOf(EffortLevels, s) >= 0;

    /// <summary>Interrupts the in-flight turn (the stream-json equivalent of Esc).</summary>
    public void Interrupt() => WriteLine(new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = NextRequestId(),
        ["request"] = new JsonObject { ["subtype"] = "interrupt" },
    });

    /// <summary>Stops one background task (a <c>run_in_background</c> shell, a Monitor, an async agent) without
    /// touching the turn: the CLI's <c>stop_task</c> control request. The task then reports
    /// <c>task_updated{killed}</c> → <c>task_notification{stopped}</c>, and Claude hears of it on its next turn.</summary>
    public void StopTask(string taskId)
    {
        if (!IsSafeToken(taskId)) return;
        WriteLine(new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = NextRequestId(),
            ["request"] = new JsonObject { ["subtype"] = "stop_task", ["task_id"] = taskId },
        });
    }

    // How long a stopped session gets to exit on its own before its tree is killed. A clean exit writes the
    // transcript's final state and runs SessionEnd hooks; a kill skips both. A bare session takes ~2.5s to exit,
    // more with MCP servers or slow hooks. The wait runs off the caller's thread.
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    /// <summary>Ends the session: closing stdin lets the CLI finish and exit; a process that lingers is
    /// killed with its tree. The session remains resumable later via <c>claude --resume</c>.</summary>
    public void Stop()
    {
        var process = _process;
        lock (_writeLock)
        {
            try { _stdin?.Close(); } catch { /* already gone */ }
            _stdin = null;
        }
        if (process is null) return;
        Task.Run(() =>
        {
            try
            {
                if (!process.WaitForExit(StopGrace)) process.Kill();
            }
            catch { /* already exited */ }
        });
    }

    public void Dispose() => Stop();

    private void WriteLine(JsonObject payload)
    {
        lock (_writeLock)
        {
            if (_stdin is null) return;
            try
            {
                _stdin.WriteLine(payload.ToJsonString());
                _stdin.Flush();
            }
            catch { /* pipe closed under us; the Exited event reports the death */ }
        }
    }

    private string NextRequestId() => $"ochre_req_{Interlocked.Increment(ref _requestCounter)}";

    // Values interpolated into the command line are restricted to bare identifier-ish tokens
    // (model names, permission modes) — never free text.
    private static bool IsSafeToken(string? s) =>
        !string.IsNullOrEmpty(s) && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_');

    internal static bool IsSafeToolRule(string? s) =>
        !string.IsNullOrEmpty(s) && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or ' ' or '(' or ')' or '*' or ':');
}

/// <summary>How to launch a controlled session. <see cref="Environment"/> entries are added to the child's
/// environment; a null value removes that variable.</summary>
internal sealed record SessionStartOptions(string Cwd)
{
    public string? Model { get; init; }
    public string? PermissionMode { get; init; }
    public string? Effort { get; init; }
    public string? ResumeSessionId { get; init; }
    public string? NewSessionId { get; init; }
    /// <summary>With a resume: keep the conversation only up to this transcript entry (how a rewind restarts).</summary>
    public string? ResumeAt { get; init; }
    /// <summary>With a resume: write the resumed conversation to a new session, leaving the source alone.</summary>
    public bool Fork { get; init; }
    /// <summary>Pins the account: injected as <c>CLAUDE_CONFIG_DIR</c>. Null inherits the host's own.</summary>
    public string? ConfigDir { get; init; }
    /// <summary>Permission rules for <c>--allowedTools</c>, run without asking (e.g. <c>Bash(ochre task *)</c>).</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    /// <summary>Text for <c>--append-system-prompt</c>; must pass <see cref="ClaudeCli.IsSafePrompt"/>.</summary>
    public string? AppendSystemPrompt { get; init; }
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();
}
