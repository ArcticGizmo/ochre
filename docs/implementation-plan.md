# Implementation plan

Status: draft, 2026-11-25. Covers M0 and M1 in detail and M2 onwards in outline; re-plan at the end of each milestone. Scope comes from [roadmap.md](roadmap.md), [spike-stack.md](spike-stack.md), [task-model.md](task-model.md), [architecture.md](architecture.md) and [security.md](security.md).

## Working stance

Ochre is an experiment, so the plan optimises for learning speed and reversibility:

- **Vertical slices.** Each step ends with something runnable that can be tried by hand, not a finished layer.
- **Dogfood from the end of M1.** Track Ochre's own work as Ochre tasks as soon as the task store and CLI exist. That is the fastest test of "does it help me know where to start tomorrow?".
- **Copy, then own.** Perch's control code is UI-free and depends only on the BCL (`ClaudeSessionController`, `StreamJsonParser`, `ControlProtocol`, `SessionEvents`, `SessionConversation`, `ControlledSessions`: about 2.4k lines at `9275e0b`), so M1 ports it rather than rewriting.
- **Defer contracts.** Don't design the module extension points (ADR 0007) before M4. Until then, keep features in plain classes behind a few seams so they can be lifted into modules later.

### Testing stance

Automated tests are written only where a bug would be expensive and silent, or where the code is already stable:

| Write tests | Why |
|---|---|
| Task file parse/serialise round trip | Corrupting a user's task file loses the one thing Ochre exists to keep. A handful of fixture files, nothing more. |
| Agent write rules (bound task only, no state change, no link removal) | Security boundary ([security.md](security.md) threat 3); cheap to test at the command-handler level. |
| Perch code copied as-is | Its tests and fixtures already exist and the code is stable, so bringing them costs little and shows when our copy drifts from Perch. Drop them if the copy is substantially rewritten. |
| Session lifecycle (start, prompts, exit, dormant, resume, restart) | Added 2026-11-25 at the user's request. `SessionLifecycleTests` drive the real `SessionManager` and controller with an in-memory `claude` (`tests/Ochre.Tests/FakeClaude.cs`) behind the `IClaudeLauncher` seam; trust, live sessions and transcripts come from the test, so nothing starts a process or reads `~/.claude`. Milliseconds, no Claude turns, and it catches regressions before a window check. |

| Don't test (yet) | Why |
|---|---|
| The M0 spike | Throwaway. |
| UI, bridge plumbing, tray, window management | Changing fast; verified by hand through the manual testing docs. |
| CLI argument handling, pipe transport | Thin; exercised by every dogfooding session. |
| "Kernel with every module off" (ADR 0007) | Deferred to M4, when the module contracts exist and settle. |

Headless options noted for later (2026-11-25), not built:

- **A `--headless` host mode** with a dev command such as `cli dev bridge <type> <json>` that sends one bridge message over the instance pipe and prints the reply. It would run real end-to-end checks (real Claude, resume, the task CLI) with no window at all; it still spends real Claude turns, so it suits exit runs. Window checks would then be needed only for visual changes.
- **The web UI in headless Chromium (Playwright) against a fake bridge**, to test the React side without WebView2. It adds a large pinned dependency, so it waits until the UI settles or regressions there start to cost.

Manual testing docs (`docs/testing/<feature>-testing.md`) stay mandatory per CLAUDE.md; they are the verification channel while automated coverage is thin, so keep them short. The stance is recorded in CLAUDE.md under Conventions.

## M0: stack spike

**Done 2026-11-25.** Outcome: ADR 0002 accepted; the Windows shell is raw WebView2 in a minimal WinForms host. Results and method are in [spike-stack-results.md](spike-stack-results.md); the spike code is preserved at the git tag `spike-stack` and has been deleted from the tree. Carried into M1 (slices 1, 3 and the terminal work in M3): coalesce terminal output on a ~16 ms timer, set `IsReputationCheckingRequired = false` for the app's own page, use a `.example` virtual host name, lazy-load Monaco, and decide bridge type generation in ADR 0009. The spike's `PseudoConsoleSession`, `BridgeDispatcher` and `WebView2Page` are reasonable starting points to lift from the tag.

Goal: accept or replace [ADR 0002](decisions/0002-stack-dotnet-host-web-ui.md) with numbers. Everything lives under `spikes/stack/` and is deleted or archived afterwards. Suggested timebox: one week; stop early if the first shell clearly passes.

### Layout

```
spikes/stack/
  ui/                 Vite + React + TS app, shared by every shell
  Spike.Core/         ConPTY wrapper, bridge dispatcher, file service, task loader, timing log
  Shell.WebView2/     WinForms + WebView2 (baseline)
  Shell.Avalonia/     Avalonia 12 + Avalonia.Controls.WebView + TrayIcon
  Shell.Photino/      Photino.NET + a tray library (only if time allows)
  measure.ps1         Samples working set of host + msedgewebview2 children
  results.md          Raw numbers and notes
```

Putting all behaviour in `Spike.Core` keeps each shell to a few hundred lines, so trying a second or third shell is cheap and the comparison is fair.

### Steps

1. **Prereqs.** Confirm .NET 10 SDK, Node LTS and pnpm versions; record them in `results.md`. Pin every UI dependency exactly (`react`, `react-dom`, `vite`, `typescript`, `@xterm/xterm`, `@xterm/addon-fit`, `@xterm/addon-serialize`, `monaco-editor`, `dompurify`) and commit the lockfile. Monaco is bundled locally (no CDN, so CSP can stay strict); configure its workers through Vite.
2. **Shared UI.** Three panes: task list (three hard-coded markdown files read through the bridge), one xterm.js tab, one Monaco editor with a diff view. A dev-only "bench" panel with buttons for the bridge round trip and throughput runs.
3. **Bridge v0.** Envelope `{ id, type, payload }` for request/response, plus host-pushed events. Host side: a dispatcher keyed on an allowlist of `type` strings, payloads deserialised with System.Text.Json source generation and `UnmappedMemberHandling.Disallow`, unknown types rejected. TS types hand-written for the spike; note how painful keeping them in sync is (input to the M1 codegen decision).
4. **ConPTY.** P/Invoke `CreatePseudoConsole` running PowerShell; output read on a background thread. Try two delivery modes: raw `PostWebMessageAsString` per read, and coalesced every ~16 ms. If both freeze under load, try WebView2 shared buffers. Expect EDR attention here; if anything is blocked, record it and stop rather than work around it.
5. **Shell 1: WinForms + WebView2.** Tray icon, single instance (named mutex plus a pipe to forward "open window"), window opened from tray, UI served with `SetVirtualHostNameToFolderMapping`, CSP with no inline or remote script.
6. **Measure shell 1** against the table in [spike-stack.md](spike-stack.md). Timings are logged from inside the app (process start to tray ready; tray click to first bridge message from the page). Bridge: 10k echo messages, record the median and p99. Throughput: `Get-ChildItem -Recurse` over a large tree and a 50 MB file, watching for long tasks in the renderer.
7. **Shell 2: Avalonia.** Same UI, same core. Check the WebView package licence first; if it is unsuitable, record that and skip.
8. **Shell 3: Photino** only if 1 and 2 are both marginal.
9. **Decide and record.** Fill in ADR 0002's spike results, mark it accepted or superseded, update `architecture.md` (named shell) and CLAUDE.md (layout no longer "planned"). Write `docs/testing/m0-spike-testing.md` describing how to rerun the measurements. Archive the spike (tag, then delete the folder).

### Things the spike must answer for M1

- Which shell, and whether its tray and multi-window story is good enough.
- Whether the bridge needs a faster channel than `postMessage` for terminal output.
- How to keep C# and TS message types in sync: hand-written, or generated from C# records (e.g. via JSON Schema). Record the choice in a new ADR (0009, bridge protocol).
- Whether hot reload of the UI inside the WebView works (Vite dev server behind a dev-only switch, never in release builds).

## M1: tasks and controlled sessions

Exit (from the roadmap): create a task, launch two agents on it, both tick plan items, quit and relaunch, and the task is intact.

### Slice 1: skeleton

- `Ochre.sln`, `Directory.Build.props` (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`), `src/Ochre.Core`, `src/Ochre.Host`, `src/Ochre.Platform.Windows`, `ui/`. Create `src/Ochre.Cli` in slice 6 and `tests/Ochre.Tests` in slice 2, when they are first needed.
- Promote the winning shell from the spike (rewrite it cleanly rather than moving spike code wholesale).
- Single instance and window activation over a per-user pipe; reuse Perch's `ControlProtocol` naming and `PipeOptions.CurrentUserOnly` pattern.
- Tray menu: Open, Quit. Closing the last window keeps the host running.
- Logging to `%LOCALAPPDATA%\Ochre\logs\`, never message bodies or tokens.

### Slice 2: task store

- `TaskDocument` model: frontmatter (`id`, `title`, `state`, timestamps, `repos`, `links`, `sessions`, `park`) and body sections (Plan, Notes, Log). Unknown frontmatter keys and extra body sections are preserved on rewrite so hand edits survive.
- Plan items parsed as nested checkbox lines with `{#id}` suffixes. Items without an id (typed by hand) get one assigned on the next host write.
- YAML: YamlDotNet is the obvious choice (the host is not NativeAOT, so reflection is fine). Pin it.
- `TaskStore` in Core: one writer queue per task (a `Channel` or `SemaphoreSlim`), read-modify-write under it, atomic write (temp file then `File.Move` with overwrite), and a log line appended for every change with its author (`user` or `session <id>`).
- `FileSystemWatcher` on the tasks folder: external edits reload the cached document and raise a `TaskChanged` event. Tolerate partial writes by retrying the read briefly; never throw out of the watcher.
- Id allocation: scan for the highest `T-NNNN` at startup and increment.
- **Tests:** round trip of 4 to 6 fixture files (minimal, full example from task-model.md, hand-edited with missing ids, unknown keys, CRLF), plus "check an item and the rest of the file is byte-identical".

### Slice 3: task list and detail UI

- Bridge v1 from the spike's findings: `tasks.list`, `tasks.get`, `tasks.create`, `tasks.update` (title, repos, notes), `tasks.setState`, `plan.check`, `plan.add`, and a `tasks.changed` event. Types generated per ADR 0009.
- List with state filter, sorted by state then last activity. Detail view: plan checkboxes, notes (markdown rendered with DOMPurify), links shown read-only, sessions list, log. Manual ordering (the `order` field) is deferred until dogfooding shows it's wanted.
- Done 2026-11-25; see `docs/testing/m1-tasks-ui-testing.md`. After the first hands-on test: tasks are created in a dialog (title, description, repos), deleted through a confirm dialog into a `.trash` folder, and hand-typed plan text and bare bullets are shown rather than ignored. UX conventions recorded in `docs/ux-conventions.md`.

### UI design pass (after slice 5)

The rest of the conventions (filter counts, search, a board view, ID under the title, a coloured status dropdown, centred content) are applied in one pass from a design artefact agreed first. It comes after the thread view so the artefact covers the task list, task detail and session thread together. Done with M2 slice 3.
- Edits from the UI go through the store like any other writer, with author `user`.

### Slice 4: controlled sessions (host side)

- Port from Perch: `ClaudeSessionController`, `StreamJsonParser`, `ControlProtocol` (the parts that apply), `SessionEvents`, `SessionConversation`, `ControlledSessions`, plus `ClaudeConfigDir`, `ClaudePaths` and `DirectoryTrust`. Bring their tests and fixtures. Commit message names Perch, the paths and `9275e0b`.
- "Start agent" on a task: choose a repo from the task's `repos` (or a folder), optional role, and launch with `OCHRE_TASK` and `OCHRE_SESSION` in the environment and the Ochre CLI's folder on `PATH`.
- Tell the agent about the task with `--append-system-prompt` (decided): a short paragraph explaining `ochre task show`, `check`, `add-step` and `note`. Without this the agents won't use the CLI, and the task isn't load-bearing.
- Record the session in the task's `sessions` list with `binding: launched`.
- Respect folder trust (`DirectoryTrust`) before launching, as Claude does.
- Done 2026-11-25 with slices 5 and 6; see `docs/testing/m1-sessions-testing.md`. Ported code is in `src/Ochre.Core/Claude/` (commit names the Perch paths). Left behind for now: Perch's `SessionLock` sidecar (one host per profile drives its own sessions; revisit with resume in M2), `SessionAttention`, the slash-command palette catalogue, and decoding transcript images from base64 (only the image cache is read). The CLI is found as `OCHRE_CLI_EXE`, then `ochre.com` or `ochre-cli.exe` beside the host, then (dev builds) this checkout's CLI build; the system prompt names it by that file name.

### Slice 5: session thread view

- Render `SessionConversation` in the web UI: user and assistant messages (streaming markdown, sanitised), tool calls folded into cards, a composer, and stop/interrupt.
- Permission prompts, `AskUserQuestion` and plan approval cards, since a controlled session is stuck without them. Port the input models (`AskUserQuestionInput`, `PlanApprovalInput`, `SessionAttention`) and use Perch's UI as the behavioural spec.
- Defer: slash-command palette, rewind, images, search. Sessions stay in memory only; whether they survive quit is M2's dormant-session work.
- Done 2026-11-25. The host owns each session's `SessionConversation` and broadcasts changed items every ~80 ms (`sessions.update`, items by index, or a reset), so a streaming reply costs a few messages a second. Streaming text shows plain and renders as markdown once complete.

### Slice 6: agent CLI

- `src/Ochre.Cli`, NativeAOT, a few hundred lines: parse args, send one JSON line over the per-user pipe, print the reply. Commands: `task current`, `show [--plan]`, `check`, `uncheck`, `add-step`, `note`.
- Host command handler enforces the rules: the session id from `OCHRE_SESSION` must be bound to the target task; agents cannot change `state` or remove links; every write logged with the session id.
- Optional hardening, cheap with named pipes: resolve the client pid (`GetNamedPipeClientProcessId`) and check it descends from a Claude process Ochre launched, so a stray process can't impersonate a session by setting env vars. Same-user processes are inside the trust boundary anyway, so this is a nice-to-have.
- UI highlights plan items added or changed by agents until the user acknowledges them.
- **Tests:** the write rules only.
- Done 2026-11-25 except the highlighting, which moves to slice 7 (the log already records each agent change). The rules live in `Ochre.Core.Sessions.AgentCommands`, reached over the instance pipe as the `agent` command; the binding is checked against the file inside the write. The client-pid check stays optional and isn't built.

### Slice 7: exit run

- Highlight plan items added or changed by agents until the user acknowledges them (carried over from slice 6).
- Walk the exit criterion by hand, write `docs/testing/m1-testing.md`, then start tracking Ochre work as Ochre tasks.
- Done 2026-11-25. The highlight is derived from the log (agent entries since the last `user reviewed agent changes`), so it adds no task fields. Walking the exit run showed agents stalling on a permission prompt for every task command, which led to ADR 0010 (pre-allow only the task commands).
- Optional cheap win to try while dogfooding: a manual "park note" field the user fills in by hand. It previews M2's value before the agent-driven version exists.

### M1 risks

| Risk | Mitigation |
|---|---|
| Agents ignore the CLI | Prompt via `--append-system-prompt`; watch it while dogfooding; MCP in M2 may be more reliable than shelling out |
| User edits a task in an editor while an agent writes | Read-modify-write under the per-task lock reloads first, so the window is small; accept last writer wins for now |
| Thread view takes longer than expected | The conversation model, stream parsing and input models come over from Perch as-is, so only the web rendering is new. Ship text, tool cards and permission prompts first; everything else waits |
| EDR flags the host spawning Claude and shells | Sign dev builds early if it bites; report blocks, never work around them |

## M2: park, resume and the morning view

Exit (from the roadmap): park two tasks at night; next morning "Today" shows both with notes; one click resumes a session with its context.

Planned 2026-11-25 from a survey of Perch main at `9275e0b`. Findings that shape it: the `session-recovery` work was squash-merged into Perch main long ago (7dcec61, "Session recovery (#39)"; design in Perch's `docs/session-recovery-plan.md`), and Perch calls dormant sessions "parked". Perch has **no MCP server** on any branch, so ours is new code. Perch's shutdown handling only stamps the time (WM_QUERYENDSESSION via Avalonia); a shutdown leaves no time to ask agents for notes. The big Perch readers (`SessionMonitor`, 1.5k lines; `TranscriptReader`, 1.5k) are not ported whole: only the functions M2 needs are extracted.

### Slice 1: dormant sessions and resume

- A session ledger in `%LOCALAPPDATA%\Ochre\sessions.json`, modelled on Perch's `SessionLedger` (`LedgerSession`: id, task, cwd, configDir, role, model, permission mode). Written atomically on launch, `/clear` (rebind) and exit; never holds message bodies.
- On host start, every session in the ledger (and every `launched` session on a task) becomes **dormant** in `SessionManager`: it shows in the task's Agents list and opens a thread loaded from its transcript. Port `TranscriptScan` (90 lines) and `TranscriptChain` (125) with their tests; history loads as Perch's `PerchSession.LoadHistoryAsync` does (resolve, active chain, last N lines, `ParseHistory` off the UI thread, `LoadHistory`, seed context tokens). Extract `ReadTitle` and `ReadContextUsage` from `TranscriptReader` rather than porting it.
- Sending to a dormant session restarts it with `--resume <id>` (same id, so `OCHRE_SESSION` and the task binding still hold), then sends. Before resuming: folder trust (as at launch), and refuse if the session is live elsewhere, read from `~/.claude/sessions/{pid}.json` (port `SessionMonitor.ReadSession`, `IProcessProbe` and `ProcessIdentity` only). That read stands in for Perch's `SessionLock`.
- Check Ochre's copies of `ClaudeSessionController` and `SessionConversation` carry #39's small edits (they were copied from main after it, so they should).
- Tests: the ported readers' own; nothing new.
- Done 2026-11-25; see `docs/testing/m2-dormant-testing.md`. `ReadTitle` and `ReadContextUsage` weren't needed yet (the context gauge fills on the first resumed turn). An agent that never got a message has no transcript, so its "resume" starts it fresh under the same id.

### Slice 2: park and resume a task

- **Park** on a task opens a dialog listing its running agents, with an optional note of your own. Confirming sets the task Parked, sends each idle agent a park turn ("write your state and next step with `ochre task park-note`"), waits for each note (busy agents: wait for the turn to end, up to a timeout, then interrupt), then stops them so they go dormant.
- `ochre task park-note "<text>"`: accepted only from a bound session while a park request for it is open, capped in length, logged. It inherits ADR 0010's pre-allow, so these limits live on the host. Tests extend `AgentCommandsTests` (security boundary).
- Storage (decided): `park.note` keeps your note first, then one line per agent (`- alpha: ...`), so the single-note format in task-model.md still reads naturally.
- **Resume** sets the task Active and shows the park note above the plan; agents stay dormant until you send to one.
- Done 2026-11-25; see `docs/testing/m2-park-testing.md`. Checked with a real agent: it wrote its note through the CLI unprompted and went dormant. The park returns as soon as the task is Parked; the window shows which agents it is still waiting on (`ParkingWaitingFor` on the task detail).

### Slice 3: Today

- A new screen: every Active and Parked task with title, park note, next unchecked step, agents (running / dormant / ended), links; sorted by last activity. One click on an agent opens its thread ready to resume. Git and worktree state wait for the git module (M4).
- Larger visual change, so it starts from a design artefact. Fold in the pending M1 "UI design pass" (filter counts, search, board view, ID under the title, coloured status dropdown, centred content) so one artefact covers list, detail, thread and Today.
- Done 2026-11-25; see `docs/testing/m2-today-testing.md`. Design in `docs/design/m2-today/`. The window opens on Today; Today and Tasks are tabs in the title bar. `TaskListItem` now carries the task's sessions, links and park time, so Today needs no new bridge call. In the status dropdown, Parked opens the Park dialog (so agents still write notes) and Done is confirmed. The board has no drag and drop; state changes go through the dropdown until dogfooding says otherwise. After the first review: Board became a title-bar tab, the thread took Perch's bubble layout, model and effort pickers went into the thread header (kept in the ledger so a resume keeps them), the cost went into a tooltip, and the task list expands to show each task's agents, their current activity and running subagents.

### Slice 4: observed sessions and binding suggestions

- List live sessions Ochre didn't start, from `~/.claude/sessions/*.json` (slice 1's reader) plus their transcripts; read-only threads by tailing (port `TranscriptTailReader`, 200 lines, with tests). Accept ADR 0006 (hook-free observation) when this works.
- Suggestions, never automatic: cwd inside a task's repo; a Jira key in the branch (port `JiraTicket.cs`, 105 lines, with tests) matching a task link. One click binds (`binding: confirmed`). `SessionWorktree` (infers a worktree from a transcript's edits) is a later refinement.
- Decided: an observed session has no `OCHRE_SESSION`, so bound observed sessions stay read-only in M2; only launched agents write to tasks. Looking the session up by parent process can come later if it's missed.
- Done 2026-11-25; see `docs/testing/m2-observed-testing.md`. The UI says "link" for bind. `SessionManager` scans every 2 s and keeps outside sessions as observed `LiveSession`s, so lists, threads and updates reuse the controlled-session path; Ochre never sends to, stops or parks them. The branch comes from the transcript's `gitBranch` (no git call), and `TranscriptTailReader` (ported with its tests) follows an open thread. `AgentCommands` now requires `binding: launched`, so a linked outside session can't use the task even if something claims its id (tested). Unlinked sessions drop off when their process ends; linked ones stay, closed, readable from the task. Not done: a `/clear` in an outside session gives it a new id, so it shows up as a new session and the old link stays closed.

### MCP server: dropped from M2

Decided 2026-11-25: launched agents use the CLI reliably once its commands are pre-allowed (M1 exit run), and a CLI is cheaper (no server process per agent, no tool definitions in every turn's context) and simpler to configure. An MCP server comes back only if a client that can't run a shell needs task access; it would forward to `AgentCommands` over the same pipe. Moved to "Later" in the roadmap.

### Slice 5: auto-park on shutdown (optional)

- Off by default (a setting). On WM_QUERYENDSESSION the host stamps the shutdown in the ledger (Perch's approach), marks each task with running agents Parked with an automatic note ("Ochre was shut down while alpha and beta were working; last step ticked: p3"), and lets the sessions end. There's no time for agent-written notes. A heartbeat after the stamp means the shutdown was cancelled, and the stamp is undone.
- Skip Perch's `PowerHistory` (Windows event log, extra package). Port `SessionEnd`'s clean/abrupt classification only if Today needs it.
- Done 2026-11-25; see `docs/testing/m2-shutdown-testing.md`. The setting lives in `settings.json` (`OchreSettings`, the first setting) behind a Settings dialog. `SessionEnding` (Platform.Windows, `SystemEvents.SessionEnding`) sits behind `ISessionEnding`. The "heartbeat" is a timer: still running 90 s after the request means it was cancelled, and only tasks still carrying their automatic note are put back. `SessionEnd`'s classification wasn't needed. Dev builds have `cli dev session-ending` to act it out.

### Slice 6: exit run

- Walk the exit criterion, write `docs/testing/m2-testing.md`.
- Done 2026-11-25; see `docs/testing/m2-testing.md`. Walked with two real agents in the dev window: parked at night with notes, restarted, both on Today with notes, one click into an agent, and a resumed agent recalled a word it was given before the park. Found on the way: paths are now normalised when a repo or agent folder is given (a doubled separator got through), and sending to an agent on a Parked task resumes the agent but leaves the task Parked (left as is, for the user to decide).

## M3: terminal tabs

Exit (from the roadmap): run a dev server in a tab, close all windows, reopen: still running. Quit and relaunch: tabs restored with scrollback.

Planned 2026-11-26. Perch has no terminal code, so everything here is new or lifted from the `spike-stack` tag (`PseudoConsoleSession`, `Native`, `OutputPump`, `terminal.ts`). The shape is in [ADR 0011](decisions/0011-host-owned-terminals.md): the host owns each PTY, filters its output, keeps its scrollback, and sends it to every window; xterm.js only draws. Terminals live in host-local state (`%LOCALAPPDATA%\Ochre\terminals\`), not in task files, because a PTY belongs to one machine and task files roam.

### Slice 1: terminals in the host

- `IPseudoConsoleFactory` in Core (`Ochre.Core.Platform`), ConPTY implementation in `Ochre.Platform.Windows` lifted from the spike: `CreatePseudoConsole`, a reader thread with a stateful UTF-8 decoder, write, resize, close.
- `TerminalManager` in Core: open (shell profile, folder, optional task, optional startup command), input, resize, close, list. Output runs through the filter, is coalesced on a 16 ms timer, appended to a per-terminal scrollback ring (about 1 M chars, trimmed by whole chunks), and raised with a sequence number so a window can join mid-stream without gaps or repeats.
- Shell profiles are detected on the host (PowerShell 7 on PATH, Windows PowerShell, Command Prompt, Git Bash if installed) and the bridge accepts only a profile id, never a command line. The startup command is typed into the shell after it starts, so it adds nothing the renderer can't already do by typing.
- `TerminalOutputFilter` (security threat 7): an allow list of OSC numbers (titles, colours, cwd, links, shell integration marks), OSC 52 and anything unknown dropped, OSC 8 targets kept only for `http`/`https`, sequences re-emitted with the filter's own terminator so xterm sees exactly what the filter checked, and an incomplete sequence held back so every chunk ends on a sequence boundary. OSC 7 and OSC 9;9 report the shell's folder, which restore uses. **Tests:** the filter, since it's a security boundary.
- Bridge: `terminals.profiles`, `terminals.list`, `terminals.open`, `terminals.attach` (scrollback and its sequence number), `terminals.input`, `terminals.resize`, `terminals.close`, `terminals.setTask`; events `terminals.output` and `terminals.changed`. Checked by hand with `cli dev eval` against the bridge before any UI exists.
- Done 2026-11-26; see `docs/testing/m3-terminals-host-testing.md`. Also `terminals.restart` (a fresh shell in a tab whose shell exited, after its scrollback). The exit line is written into the stream, so late windows and restored tabs show it. Input is queued to a writer thread, so a shell that stops reading never blocks the UI thread. ConPTY re-emits OSC 8 itself (adding an `id`), and passes OSC 52 through, so the host filter is what stops it.

### Slice 2: terminal tabs in the window

- Larger visual change, so it starts from a design artefact agreed first (`docs/design/m3-terminals/`): where tabs live, how a task shows its terminals, the new-terminal dialog, link hover.
- xterm.js (`@xterm/xterm`, `@xterm/addon-fit`, `@xterm/addon-web-links`, pinned) lazy-loaded like Monaco would be, so it stays off the window-open path. No clipboard addon, so OSC 52 has nowhere to land even if the filter missed one.
- Links: hovering shows the target; Ctrl+click opens it in the browser through the host, which re-checks the scheme; middle click opens a new browser window (UX conventions).
- Input from a window is sent only while that window has focus, and never while it replays scrollback, so query replies from a second window or a replay don't reach the shell. PTY size follows the window that last resized it.
- Terminals attach to a task: opened from the task (folder from its repos), or attached and detached from the tab's menu. The task view lists its terminals.
- Done 2026-11-26; see `docs/testing/m3-terminal-tabs-testing.md`. Design agreed as a first pass (Terminals title-bar tab; the task lists its terminals). Links open through a new `app.openUrl` bridge call, checked by `OpenTargets.WebUrl` on the host. Input counts as the user's when the window has focus or within a second of a key, paste or click in that terminal; relying on `document.hasFocus()` alone risked silently dropping typing. xterm is a separate 337 KB chunk loaded on first use. An empty folder opens in the home folder. The "keep scrollback" switch moves to slice 3, where it does something.

### Slice 3: restore across quit

- `terminals.json` (tabs in order, profile, folder, task, startup command, title) written atomically on every change; scrollback written encrypted with DPAPI (`IDataProtector` in Core, `ProtectedData` in Platform.Windows) every 30 s while it changes and on quit. A per-tab "Don't keep scrollback" switch (security threat 6) keeps nothing on disk for that tab.
- On host start every saved tab comes back with its old scrollback, a "restored" divider, and a fresh shell in its last known folder. The startup command is **not** re-run (explicit over magic); the tab offers "Run `<command>` again".
- Closing every window leaves PTYs running (ADR 0008); quitting closes them, which ends the shells and the programs attached to their consoles. If hand testing shows orphaned dev servers, put each terminal in a Job Object.
- Done 2026-11-26; see `docs/testing/m3-restore-testing.md`. DPAPI is called directly (`CryptProtectData`, with Ochre's own entropy) rather than through a package. No orphans after a quit, so no Job Object. A list that exists but can't be read is never overwritten that session, and only scrollback files of tabs this session knew are deleted. ConPTY clears the screen when a shell starts, which erased the restored output: xterm's `scrollOnEraseInDisplay` now pushes cleared screens into scrollback (so `cls` keeps history too), the divider clears and homes the cursor before drawing, and a restored tab opens scrolled to the divider. Terminals are also saved when Windows ends the session. After the user's first test (restore ignored a `cd`), the shells Ochre starts report their folder at each prompt with Windows Terminal's OSC 9;9, wrapping the user's own prompt: PowerShell through a short `-Command` run after the profile (not a script file, which execution policy can block, and not `-EncodedCommand`), cmd through `PROMPT`, Git Bash through `PROMPT_COMMAND`. A folder change saves the list within 300 ms, so it survives a force-close. **Tests:** `TerminalPersistenceTests` (data loss and threat 6).

### Slice 4: exit run

- Walk the exit criterion by hand, write `docs/testing/m3-testing.md`.
- Done 2026-11-26; see `docs/testing/m3-testing.md`.

### Not in M3

- "View as terminal" for an agent's thread (roadmap, "to look at"): stays unbuilt until dogfooding asks for it.
- Split panes, search in scrollback, per-tab themes.

## M4: module host and first modules

Exit (from the roadmap): turn every module off and the kernel still works; turn sprig on and a task's worktree is a sprig slot; two tasks open in two windows on two virtual desktops come back on those desktops after quitting and relaunching.

Planned 2026-11-26 from a survey of Perch main (still `9275e0b`) and the sprig CLI. Findings that shape it: Perch's git code (`GitRunner` 289 lines, `GitCheckoutScanner` 137, `GitWorktrees` 107, `WorktreeLayout` 193) and sprig code (`SprigCli`, `SprigStatus`, `SprigStatusFold`, 680 lines with 38 tests) are UI-free and port cleanly. The git graph is about 2.9k lines plus a 1.1k-line `GitRepoService`, so it gets its own slice and ports only what the loader calls. Perch has no Jira REST code; that module is new. sprig answers every command with `--json` (`checkout -b`, `workspaces`, `watch`, `free`, `logs`), with failures as `{ok:false, code, error}`. The contracts are in [ADR 0012](decisions/0012-module-contracts.md).

Slices are ordered so the exit criterion's three parts come first; the Jira module and git graph follow.

### Slice 1: module host

- `IModule` and `IModuleContext` in Core (ADR 0012). A module contributes bridge handlers with their contract entries, events, shutdown and session-ending hooks, and (from slice 2) extension points. Which modules are on is kept in `settings.json` (`modules: { id: bool }`), read at start; a change takes effect on restart, and Settings offers "Restart Ochre now".
- First-party modules live in `modules/<Name>/` as their own projects, referencing Core (and the platform services handed to them through the context). The bridge contract is assembled from the kernel's list plus each loaded module's; BridgeGen reads every module so the UI's types never depend on what's switched on.
- The UI gets a module registry (`ui/src/modules/`): each module declares its contributions (a title-bar view, a task-detail section), and the app renders those of modules `app.info` reports as on.
- Move the terminal behind a module (`terminal`, on by default): Core's terminal code, its bridge API and messages, its tests. ConPTY and DPAPI stay in `Ochre.Platform.Windows` behind Core interfaces.
- Auto-park on shutdown becomes a module (`shutdown-park`, off by default), replacing its setting; an existing `autoParkOnShutdown: true` turns the module on once.
- **Tests:** the kernel with every module off (task store, session lifecycle with the fake `claude`, agent commands) runs through `ModuleHost` with nothing loaded; a module that throws on start is reported and left off rather than taking the host down.
- Done 2026-11-26; see `docs/testing/m4-modules-testing.md`. `SessionManager` is internal to Core, so the module contracts are internal and shared with the module assemblies, the host and BridgeGen through `InternalsVisibleTo` (ADR 0012, Consequences). `Empty`, `Ack` and the contract records moved to `Ochre.Core.Bridge`; BridgeGen accepts records from the host's, Core's and each module's `Bridge` namespace and refuses two records with one name. Restart quits, releases the instance pipe and mutex, then starts a new host. The Terminals view now derives its selected tab rather than writing a fallback back, so a just-opened tab isn't lost to a list fetched a moment earlier.

### Slice 2: git module and worktrees

- Port `GitRunner`, `GitCheckoutScanner`, `GitWorktrees` (and the parts of `GitRepoService` that read status) with their tests. Commit names Perch, the paths and `9275e0b`.
- Show each task repo's branch, dirty count and ahead/behind on the task and on Today (deferred from M2), refreshed on window focus and after an agent's turn, never on a tight timer.
- Kernel extension point `WorktreeProvider` (ADR 0012): create, list, remove a checkout for a task. The git module provides `git-worktree` (`git worktree add` under a `WorktreeLayout` path). "Start agent" and "Open terminal" can start in a new worktree for the task; the task records it (a `worktrees` list in its frontmatter, preserved by older versions as an unknown key).
- With the git module off, the task shows its repos as plain folders and agents start in them, as today.
- Done 2026-11-26; see `docs/testing/m4-git-testing.md`. Ported from Perch `9275e0b`: `GitRunner` whole, `GitWorktrees` whole, the status parser and its models from `GitRepoService`/`GitRepoModels`, `WorktreeLayout` without its presets, choices and exclude-file helpers, and only `FindRoot`/`ConfigValue` from `GitCheckoutScanner` (the remote parsing waits for a feature that needs it). Decisions made while building:
  - **Layout root**: inferred from the repo's existing worktrees or an ignore-file hint, as in Perch; with neither, `{parent}/{repo}.worktrees/{name}` beside the repo rather than Perch's `{root}/.claude/worktrees`, so the main tree never lists worktrees as untracked and tools walking up for CLAUDE.md don't also find the main checkout's copy. No exclude-file writes are needed.
  - **The extension point**: `IWorktreeProvider` (plan, create, check, remove) added through `IModuleContext.AddWorktreeProvider`; `ModuleHost` exposes only the running modules' providers. The task's `worktrees` list is the one list of a task's checkouts, so "list" is reading the task rather than asking providers; a provider is asked to check (what removal would lose) and remove a checkout it made. Kernel bridge `worktrees.*` validates that the repo is one of the task's and that removal names a worktree the task records.
  - **Starting in a worktree**: the start dialogs call `worktrees.create`, then the unchanged `sessions.start`/`terminals.open` with the new folder; a retry reuses the worktree rather than making another.
  - **Removal**: a confirmed dialog. Uncommitted changes need their own tick (`git worktree remove --force` only then); "also delete the branch" is ticked by default only when no commit on it is missing from every other branch and remote (`rev-list --count <b> --not --exclude=<b> --branches --remotes`), and warns with the count otherwise. Removal is refused while an agent runs in the folder. On Windows, git deletes a worktree's files and record before failing on a folder a shell holds open; the provider recognises that half-removed state, says what to close, and a second remove deletes the empty folder (never one with files in it).
  - **Refresh**: `git.status` per task, read when a window takes focus or is shown, when the task changes, and on `git.changed` after a session's turn ends (busy to not busy); the host keeps each folder's answer for 3 seconds so several windows share one git run.
  - Also: an outside session in a task's worktree is suggested for that task ahead of tasks that only share the repo; `ochre task show` lists worktrees. A dev-only `OCHRE_DEV_HEADLESS=1` opens windows off-screen without a taskbar button or focus, for driving the UI without disturbing the user.

### Slice 3: sprig module

- Port `SprigCli`, `SprigStatus`, `SprigStatusFold` with their tests. A second `WorktreeProvider`, `sprig`: `sprig checkout -b <branch> --json` makes the slot; the task's worktree records `provider: sprig` and the workspace key. Nothing starts by itself; "Start apps" runs `sprig up`.
- One `sprig watch --json` process while the module is on feeds app status (state, port, readiness) to the task and Today; a logs panel tails an app's `logFile` (read with `FileShare.ReadWrite`). "Give slot back" runs `sprig free`, confirmed in a dialog. Errors show sprig's own `code` and message.
- Which provider a new worktree uses is chosen in the dialog when both are on; the task remembers its last choice.

### Slice 4: multiple windows

- Open a task in its own window (task context menu, a button on the task, `ochre open T-0042`). A window remembers its task (or none), size, position and virtual desktop (`GetWindowDesktopId`) in `%LOCALAPPDATA%\Ochre\windows.json`; on relaunch each window reopens on its desktop (`MoveWindowToDesktop`), falling back to the current one if that desktop is gone.
- Opening a task already shown in a window on another desktop asks: bring that window here, or open another view here. It never switches desktops.
- Position is per monitor: port `PlacementMath` and its tests, so a window whose monitor is gone comes back on the primary.

### Slice 5: Jira module

- New code. `TaskSource`: import an issue as a task (key, summary, description as notes, link back). `LinkResolver`: title, summary and live status for a `jira` link, shown on the task; `ochre task fetch <link>` returns detail marked as untrusted (security threat 2).
- Credentials: site, email and an API token, the token in Windows Credential Manager (`CredWrite`/`CredRead` behind a Core `ISecretStore`), never in settings or logs. Set up from the module's settings section.
- Needs a Jira site to test against; ask before this slice starts.

### Slice 6: git graph

- Port the graph model (`GraphModels`, `LaneAllocator`, `GraphSnapshot`, `GraphLogReader`, `GraphLoader` and the status calls they need) with `GitGraphTests`. Render it in the web UI (rows, lanes and edges as SVG, refs, worktrees and stashes), paged 2,000 commits at a time. Read-only first; write operations (`GitOps`) and avatars wait.
- Larger visual change: starts from a design artefact.

### Slice 7: exit run

- Walk the exit criterion by hand, write `docs/testing/m4-testing.md`.

### Not in M4

- Third-party plugins (M7). The contracts are shaped so they can be served out of process later, but nothing loads code that isn't built into Ochre.
- Live module toggling without a restart; git write operations from the graph; graph avatars.

## M5 onwards (outline)

Re-plan each milestone in detail when it starts. Notes worth keeping now:

- **M5 to M8** stay as written in the roadmap; too far out to plan usefully.

## Housekeeping before starting

- Work continues on the `rename-to-ochre` branch for now; no per-milestone branches.
- New ADRs expected: 0009 bridge protocol and type sync (after M0); the shell choice goes into ADR 0002 itself.
