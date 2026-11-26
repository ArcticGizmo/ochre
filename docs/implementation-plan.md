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

Slices are ordered so the exit criterion's three parts come first; the git graph and then the Jira module follow.

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
- Done 2026-11-26; see `docs/testing/m4-sprig-testing.md`. Ported from Perch `9275e0b`: `SprigCli`, `SprigStatus`, `SprigStatusFold` and `SprigSessionLink`'s folder matching (as `SprigFolders`), with their tests; `SprigNotices` stayed behind (Ochre has no notifications yet). Verified against sprig 0.9.15. Decisions made while building:
  - **Module off by default** (the exit criterion turns it on; it needs sprig installed).
  - **Git primitives moved to Core** (`src/Ochre.Core/Git/`: `GitRunner`, `GitStatusReader`, `GitWorktreeScanner`, `GitCheckoutScanner`, and a new `GitBranches` for name checks, existence, unique-commit counts and deletion), since both providers need them and modules don't reach into each other. CLAUDE.md already placed git in Core.
  - **Contract additions**: `TaskWorktree.Key` (the sprig workspace key, written as `key:`); a plan's `Path` may be null (sprig picks the slot); a provider names its remove action and describes it (`RemoveLabel`, `RemoveDescription`), and a check says whether it can discard changes (sprig can't: `sprig free` refuses a dirty slot, so the dialog asks to commit or stash).
  - **Making a slot** is `sprig checkout -b <branch> --json` run in the repo, never with `--trust`; the planned check runs `sprig info --json` there first so "not on the Repos list" and "untrusted" show before anything is made. Sprig's answers are pretty-printed across lines, which Perch's line-by-line parser missed; the parse now reads the last top-level object wherever it starts.
  - **Giving a slot back** is `sprig free` (the slot, its data and folder stay sprig's), then `git branch -D` when asked. Refused until the watch has sent its first snapshot, so a slot is never skipped as "already free".
  - **Status**: one `sprig watch --json` while the module is on, restarted with backoff if it exits and looked for again every minute when sprig isn't installed; changes broadcast as `sprig.changed` after a 300 ms settle. A task shows every non-free workspace one of its folders is in (deepest dir wins), main checkouts included. Actions are Start/Restart/Stop (`start --no-wait` rather than the plan's `up`, as Perch did) and Open in sprig; the bridge acts only on workspaces the task's folders are in, and reads only the log file sprig names.
  - **Remembering the kind**: the dialog defaults to the provider of the task's most recently made worktree; no new frontmatter.

### Slice 4: multiple windows

- Open a task in its own window (task context menu, a button on the task, `ochre open T-0042`). A window remembers its task (or none), size, position and virtual desktop (`GetWindowDesktopId`) in `%LOCALAPPDATA%\Ochre\windows.json`; on relaunch each window reopens on its desktop (`MoveWindowToDesktop`), falling back to the current one if that desktop is gone.
- Opening a task already shown in a window on another desktop asks: bring that window here, or open another view here. It never switches desktops.
- Position is per monitor: port `PlacementMath` and its tests, so a window whose monitor is gone comes back on the primary.
- Done 2026-11-26; see `docs/testing/m4-windows-testing.md`. Ported from Perch `9275e0b`: `PlacementMath` (as `Ochre.Core.Layout.PlacementMath`, its `OverlayPlacement` reduced to a `Placement` record, `BesideOverlay` left behind) with its tests. Two-desktop restore checked by hand; the headless run covered task windows, restore and the missing-desktop fallback on the dev desktop. Decisions made while building:
  - **How a window says what it shows**: both. The shell hands each page's bridge its window (`IWindowHost.Connect(IShellWindow, post)`); the page asks `window.get` for the task it opened on, and reports `window.setTask` whenever the task it shows changes (the selected task on the Tasks view, else none). The host validates the id and keeps it on the window; that is what's saved and what "already open" matches. No URL parameter.
  - **Threads aren't restored**: a window reopens on its task, not on an agent's thread that was open in it; the thread is a click away and its agent may be gone.
  - **"Already open"** excludes the window that asked, so "Open in new window" from a window showing the task still opens another. A window here comes forward; only windows on other desktops show the dialog (bring here, or open another here). Bringing a window moves it to the asking window's desktop, or the foreground window's (the CLI's terminal), since Windows has no documented "current desktop" call; if neither can be read, a new window opens instead.
  - **`ochre-cli open T-0042 [--bring|--new]`**: asks on the console when only another desktop shows the task, or explains the flags when input is redirected. With Ochre not running it starts it in the tray, so only the task's window opens beside the restored ones.
  - **The layout** (`windows.json`, Core's `WindowLayoutStore`) is read by the first window opened, not at start, so a tray-only start that quits without opening anything keeps the saved windows. It's saved a second after any open, close, move, resize, activation (a move between desktops raises nothing else) or task change, and synchronously on quit and when Windows ends the session. Closing a window yourself drops it from the layout. Reopened windows are shown without activation, since activating one on another desktop would switch to it.
  - **Placement** saves the normal bounds with the monitor, work area and scale. An unchanged monitor gets the exact bounds back; a changed one (or the primary, when nothing overlaps where the window was) gets the same DIP distance from the nearest corner via `PlacementMath`, shrunk to fit.
  - **`OCHRE_HIDE_ON_CLOSE` is removed**: a hidden window would hold a task view nobody can see and fight the saved layout, and a cold window opens in about 1.3 s. `window.shown` stays, now sent when a minimised window or one brought from another desktop comes back into view.
  - **Dev settings**: `OCHRE_DEV_DESKTOP` stands in for the user's current desktop ("here" is that desktop, and windows are brought to it), while saved desktops are honoured on restore. Headless alone keeps every window "here", and its off-screen position is never saved. `ochre-cli dev windows`, `dev move-window <id> <guid>`, and `--window <id>` on `dev eval`/`dev capture` drive several windows. The instance pipe could answer before the message loop started; shell calls now wait for it.
  - **Not built here**: the roadmap's "jump to task" hotkey.

### Slice 5: git graph

- Port the graph model (`GraphModels`, `LaneAllocator`, `GraphSnapshot`, `GraphLogReader`, `GraphLoader` and the status calls they need) with `GitGraphTests`. Render it in the web UI (rows, lanes and edges as SVG, refs, worktrees and stashes), paged 2,000 commits at a time. Read-only first; write operations (`GitOps`) and avatars wait.
- Larger visual change: starts from a design artefact.
- Done 2026-11-26; see `docs/testing/m4-graph-testing.md` and the agreed design in `docs/design/m4-graph/`. Model ported from Perch `9275e0b` into `src/Ochre.Core/Git/Graph/`, with the matching `GitGraphTests` and the base-ref ranking (`GitBaseRefs`, from `GitRepoService`) for the Branch scope. Left behind with their tests: checkout and drop planning, `GitOps`, branch visibility (hidden branches), tag trees, commit info and avatars. Decisions made while building:
  - **Where it lives**: a **Git** title-bar view from the git module, its parameter (task, folder, scope, base) kept per window, and a **Graph** link the module adds beside each git folder on a task's page (not on Today). A repo opens All; a worktree opens This branch.
  - **Untrusted renderer**: `graph.load` names a task and one of its own folders. It returns a token; `graph.more`, `graph.commit` and `graph.changes` name that token, so git runs only in that checkout, for a full hash the graph showed or a worktree it listed. A base branch must be one `for-each-ref` listed, so nothing the page sends reaches git as an option. The host keeps the 16 most recent loads; an expired one makes the page reload.
  - **Details**: the whole message (`git show -s`, one call per click) with hard-wrapped prose rejoined, and a worktree's changed files (`git status`). Changed files of a commit and diffs wait for the editor (M6).
  - **Rows**: virtualised at a fixed 28 px; each row draws the band above it, so any window of rows paints on its own. Lane colours are Perch's dark set, by lane. This branch shows ahead/behind its base (`rev-list --left-right --count`).
  - **Refresh**: on window focus (at most every 5 s), after an agent's turn on the task (`git.changed`), and from a button; the selection is kept when its commit is still there.
  - A window on the Git view reopens on Today after a restart (slice 4 saves a window's task only).

### Slice 6: Jira module

- Moved after the git graph on 2026-11-26 at the user's request; it isn't part of the exit criterion. Then moved out of M4 to the roadmap's Later list the same day, also the user's call; the plan below stands for when it's scheduled.
- New code. `TaskSource`: import an issue as a task (key, summary, description as notes, link back). `LinkResolver`: title, summary and live status for a `jira` link, shown on the task; `ochre task fetch <link>` returns detail marked as untrusted (security threat 2).
- Credentials: site, email and an API token, the token in Windows Credential Manager (`CredWrite`/`CredRead` behind a Core `ISecretStore`), never in settings or logs. Set up from the module's settings section.
- Needs a Jira site to test against; ask before this slice starts.

### Slice 7: exit run

- Walk the exit criterion by hand, write `docs/testing/m4-testing.md`.
- Done 2026-11-26, ahead of slice 6 (the exit criterion doesn't need Jira); see `docs/testing/m4-testing.md`. Every module off (including a real agent on a task), and a sprig slot made and given back from a task on InFlight.NextGen, walked in the dev host; windows on two desktops by the user. Found on the way: sprig can make a slot and then refuse it, which left a slot and branch nothing tracked; the sprig provider now records the slot it finds for the branch, so the task shows sprig's error and can give it back.

### Jump to task

- The roadmap's "jump to task" hotkey, done after the exit run: a system-wide shortcut that brings up an Ochre window on the current desktop (or opens one) with a picker of tasks, and Ctrl+K for the same picker inside a window.
- Done 2026-11-26; see `docs/testing/m4-jump-testing.md`. Decisions made while building:
  - **Ctrl+Alt+Space** by default (the user's pick), kept in `settings.json` as `jumpHotkey` ("" turns it off) and changed in Settings, where it applies at once; Core's `Hotkey` parses it (at least one of Ctrl, Alt or Win; a letter, digit, F1 to F24 or Space) and says why another app's registration or a bad entry stops it.
  - **Kernel and shell, not a module**: the picker is navigation like the title bar, and the hotkey needs the shell's windows, which modules can't reach (ADR 0012). Registered with `RegisterHotKey` on a message-only window on the UI thread; receiving the hotkey is what lets Windows bring Ochre's window forward.
  - **Enter shows the task in the window the picker is in; Ctrl+Enter opens it in a window of its own** (slice 4's rules: one here comes forward, one elsewhere asks). Done tasks appear only once you search.
  - **Ctrl+K leaves terminals alone**: a focused terminal keeps it for the shell (clear to end of line).
  - **Dev runs on another desktop or headless don't register it**, so a test run never takes the user's shortcut; `ochre-cli dev jump` does what pressing it does, and Settings says it isn't registered.
  - A window the hotkey opens learns it from `window.get` (`jump: true`); one already open gets a `window.jump` event sent to it alone.

### Not in M4

- Third-party plugins (M7).
- The Jira module (slice 6), moved to the roadmap's Later list. The contracts are shaped so they can be served out of process later, but nothing loads code that isn't built into Ochre.
- Live module toggling without a restart; git write operations from the graph; graph avatars.

## M5: attention inbox and Roost grid

Exit: start three agents on a task and open its grid: all three stream side by side, and a permission prompt is answered in its pane. From another task's window, the inbox shows a question from one agent and a finished turn from another, grouped by task; answering the question there and opening the finished agent's thread clears both from every window. An outside `claude` in the task's repo, linked to the task, appears in the grid read-only, and the inbox lists it while it waits. With Ochre's windows in the background, a permission prompt pops a notification under its task's name; Allow on the notification lets the agent carry on, and a prompt answered inside Ochre takes its notification away. With the `roost` and `notifications` modules off, the inbox still works.

Planned 2026-11-26 from a survey of Perch main (still `9275e0b`) and Ochre's session model. Scope agreed with the user: the grid shows one task's agents (a grid across tasks waits for dogfooding to ask), failed CI waits for a GitHub module (roadmap, Later), and rich desktop notifications are in, starting with a spike. Where each piece lives is [ADR 0013](decisions/0013-attention-inbox-and-roost.md): the inbox is kernel; the grid and notifications are modules.

Findings that shape it:

- **Ochre already has every input the inbox needs.** A controlled session's `SessionConversation` holds the pending permission (`IsQuestion`, `IsPlan` tell a question or plan approval apart), `WaitingElsewhere`, `TurnActive` and `LastTurn`; an observed session has `ObservedStatus` (busy, idle, waiting, closed) from `sessions/{pid}.json`; `sessions.update` already reaches every window. What's missing is "seen": nothing records that the user has looked at a finished turn.
- **Perch has no unified inbox.** Its Roost rail is the nearest thing (urgency groups: needs you, longest waiting first; then done, to review; then working; then quiet), with `NextNeedingYou` and `AttentionSeen` (a turn counts as seen only when its window is active and its pane is on screen). Those rules carry over; the rail itself doesn't, since the inbox replaces it.
- **Perch's Roost model is UI-free**, about 1.7k lines with about 150 tests in `src/Perch.Core/Data/Roost/`. `RoostGridLayout` (347 lines: regions on a 12×12 grid, at least 2 units each way, at most 8; split, merge, dividers, reading order) and `RoostTemplates` (95: nine presets and `ForCount`) port as they are. `RoostTabSet` (607) is tabs plus placement; only its placement rules are wanted (assign with swap, unassign, zoom, sync when a session goes). `RoostRoster` (439) mostly exists to give pid-keyed sessions stable keys across resume, take-over and `/clear`; Ochre's session ids already survive all three, so it isn't ported. `RoostToken`, `RoostKeys`, `RoostLayoutLibrary` and tabs aren't needed in M5. The Avalonia window, `SessionPane` and `RoostFeed` are the behavioural spec: a pane is a compact live thread, not a terminal; Perch's own sessions are interactive, outside ones read-only; nothing is placed or moved without the user.
- **Panes can share the existing feed.** `sessions.update` goes to every window for every session, and an observed session's transcript is followed once anything has opened its thread, so N panes are N filters on one stream rather than N subscriptions.

- **Perch's notifications are plain** (`NotificationService`: toast, chime, ntfy for done, waiting and failed), so there's nothing to port for rich ones. Ochre's platform project targets `net10.0-windows` without the WinRT projections toasts need, Ochre has no installer or Start menu shortcut to carry an AppUserModelID, and a notification clicked after the host has quit needs a registered activator. Those are the unknowns the spike settles.

Slice 1 is the notifications spike, which needs nothing else; slices 2 to 4 are the inbox and notifications, 5 and 6 the grid. The inbox and the grid don't depend on each other.

### Slice 1: notifications spike

- Throwaway code in `spikes/notifications/` (never referenced by `src/`), findings in `docs/spike-notifications-results.md`, an ADR if the answer changes the platform project. Shows real notifications, so it runs with the user at the screen; the user clicks the buttons.
- Questions it answers:
  - **API**: WinRT `ToastNotificationManager` (needs `net10.0-windows10.0.19041.0` and CsWinRT) against Windows App SDK `AppNotificationManager` (unpackaged, self-contained). Cost of each in output size, startup and build, and which works without an installer.
  - **Identity**: an AppUserModelID registered under `HKCU\Software\Classes\AppUserModelId` (name and icon) without a Start menu shortcut; a separate id for the dev profile so its notifications never look like the installed Ochre's.
  - **Content**: a header per task (Windows groups notifications under it), buttons (Allow, Deny, Open), a text box with a Send button (reply to a finished agent, feedback on a plan), a choice box (a question's options), an app logo per status, and the `urgent` or `reminder` scenario for a waiting permission (does it stay on screen; does it get through Do Not Disturb).
  - **Activation**: while the host runs, a button press reaches it in-process from the pop-up and from the notification centre, with the arguments and the typed text. After the host has quit: what a click does with a COM activator (`LocalServer32` under HKCU) or a protocol (`ochre:`), and whether the endpoint protection on this machine objects to either. If both are trouble, a click on a stale notification simply starts Ochre.
  - **Lifecycle**: replace a session's notification in place (tag and group), remove it when it's answered elsewhere, clear a task's group, and what Windows keeps in the notification centre after a restart.
- Out of scope for the spike: macOS, sounds beyond Windows' default.
- Run 2026-11-26; see `docs/spike-notifications-results.md`. The WinRT path works unpackaged from an HKCU AppUserModelID alone: task headers, styled buttons, choice and text boxes, logos per status, progress updated in place, replace and remove by tag and group, and clicks with typed input arriving in-process. Still to check before slice 4: a click after the process has quit (protocol activation, which would avoid registering a COM activator), clicks from the notification centre, and `urgent` against Do Not Disturb.

### Slice 2: attention model (host)

- `src/Ochre.Core/Attention/`: an `AttentionBoard` derives the items from `SessionManager` whenever sessions change; it stores only "seen" and "dismissed" marks, in memory. Kinds:
  - **needs you** (controlled): a permission prompt, a question, a plan approval, or `WaitingElsewhere` (Claude wants something Ochre can't show, so the item opens the thread).
  - **waiting** (observed): `ObservedStatus` is waiting. Read-only; the item opens its thread and says where it runs.
  - **finished** (controlled): a turn ended (busy to idle) and hasn't been seen since; carries the last lines of the reply. A failed turn (an error result, or the process exiting with a non-zero code) is the same kind, flagged as failed.
- Each item has its session, task (empty for an unlinked observed session), kind, a one-line summary and `since`. Order: grouped by task, groups by their oldest needs-you item, then by their oldest finished one; within a group, needs you before finished, longest waiting first.
- **Seen**: port `AttentionSeen` with its tests (Perch `src/Perch.Core/Data/Roost/AttentionSeen.cs`). A window reports `attention.seen {sessionId}` when a session's thread or grid pane is on screen while the window is active; that clears its finished item. Answering a permission clears its item through the existing `sessions.respond`. `attention.dismiss {itemId}` drops a finished or waiting item until the session's next change of state. The host checks every id against its current items.
- Bridge: `attention.list` and an `attention.changed` event, coalesced like `sessions.update` and broadcast to every window. Run BridgeGen.
- **Tests** (controlled-session lifecycle, with `FakeClaude`): a permission raises a needs-you item and answering clears it; a turn's end raises finished, seen clears it, the next turn raises it again; an exit with an error raises failed; an observed session's waiting status raises and clears its item; dismiss and seen with an id the host never listed are refused.
- Done 2026-11-26; see `docs/testing/m5-attention-testing.md`. `AttentionBoard` in `src/Ochre.Core/Attention/`, `AttentionApi` in the host, 8 tests in `AttentionBoardTests`. Decisions made while building:
  - **`AttentionSeen` isn't ported.** Whether a thread or pane is in front of the user is something only the page knows (window focus, what's on screen), so the rule lives in the UI, which calls `attention.seen`; on the host it would be a two-argument `||`.
  - **What "finished" is**: a new `LastTurn` on a controlled session, or a process exit with a non-zero code (`LiveSession.LastExit`, new). A turn starting clears the last one's finished item whether or not it was seen, so sending again counts as having looked. History loaded from a transcript sets no `LastTurn`, so nothing from before a restart is raised.
  - **Dismiss** is allowed for finished, failed, waiting (outside) and waiting elsewhere, never for a permission, question or plan, which must be answered. A dismissed wait stays hidden until the session needs something else.
  - **`attention.changed` carries the whole list**, which is short, so windows need no follow-up fetch. `attention.seen` refuses a session the host doesn't have; seeing a session with nothing finished is fine (the page calls it whenever a thread is in view).

### Slice 3: inbox view

- Larger visual change: starts from a design artefact the user agrees first (`docs/design/m5-inbox/`).
- A kernel title-bar view, **Inbox**, with a badge counting needs-you items (finished ones show in the view, not the badge). Groups by task, each with the task's title and a link to it; an unlinked observed session sits under "Not on a task".
- A permission is allowed or denied in place; a question or plan approval is answered in place with the thread's own cards; anything else opens the thread in this window. A finished item shows the reply's last lines with Open and Dismiss.
- Ctrl+. in any window goes to the next item needing you (the inbox's order, wrapping), opening its thread. The tray tooltip says how many need you.
- The task list and Today keep their "needs you" status; they draw it from the same items so the counts agree.
- Done 2026-11-26; see `docs/testing/m5-inbox-testing.md` and the agreed design in `docs/design/m5-inbox/`. Decisions made while building:
  - **The pending prompt rides on the item** (`AttentionItemDto.Prompt`, the thread's own `PermissionDto`), so a question or plan is answered in place without fetching the thread.
  - **Reject with feedback**: `sessions.respond` gained `denyMessage`, passed to Claude as the denial's message (the controller already took one); ignored with an allow, at most 4,000 characters.
  - **Seen** is reported by `SessionView` itself (on open, on window focus, and when a turn finishes while it's shown), only while its window has focus.
  - **Answered elsewhere**: a prompt that leaves the list without being answered in this inbox lingers for a moment as "No longer waiting: answered elsewhere, or the agent moved on". The design's "Answered in another window: …" with the answer would need the host to say who answered; not worth it yet.
  - **"Needs you" on chips** stays the session status (`waiting`), which is the same condition the board reads, so the counts agree without the task list reading the board. A dismissed outside wait still shows "needs you" on its chip, which is true.
  - **Tray**: `IShell.SetTrayStatus` adds "N need you" to the tray tooltip.
  - **Link to a task** for an unlinked outside session goes to Today, where the outside sessions and their suggested tasks are listed.

### Slice 4: notifications

- Built on the spike's answers. The `notifications` module (on by default) watches the attention board and shows a Windows notification per session through a Core `IDesktopNotifier` with its implementation in `Ochre.Platform.Windows`. One notification per session, replaced as its state changes and removed when its item clears, all under a header naming the task.
- By kind:
  - **Permission**: what it wants to run, shortened; Allow, Deny and Open.
  - **Question**: a single-choice question with up to five options gets a choice box and Answer; anything else gets Open.
  - **Plan approval**: Approve, and a feedback box with Reject; Open.
  - **Finished**: the reply's last line; a text box with Send (sends the next prompt) and Open. A failed turn says so, with Open.
  - **Waiting** (observed): where it runs and Open; nothing else, since it's read-only.
- Nothing shows for a session whose thread or grid pane is on screen in the active window (the `AttentionSeen` rule). Settings: which kinds notify (all on except observed waiting), and on or off per kind.
- **The renderer and Windows are both untrusted callers here.** A notification's arguments carry only the item id and the action; the host acts only if that item is still current and the action fits its kind, so a stale Allow does nothing. Typed text goes through the same path as the composer.
- **Privacy**: Windows keeps notifications in its own store, so the body is a short summary (the tool and a shortened command), never a whole command or reply.
- **Dev profile**: a headless dev host never shows notifications on the user's screen; it logs them and `ochre-cli dev notifications` lists what it would have shown and can press a button, so the module is driven without the screen. `OCHRE_DEV_NOTIFY=1` shows them for real.
- **Tests**: the host's handling of an action (current item, stale item, wrong action for the kind, unknown id) with `FakeClaude`.
- Done 2026-11-26; see `docs/testing/m5-notifications-testing.md`. `Ochre.Platform.Windows`, the host and BridgeGen now target `net10.0-windows10.0.19041.0` (run.cmd and cli.cmd follow the new output folder). Decisions made while building:
  - **Every button is handled in-process** (foreground activation), not as an `ochre:` link, though the spike showed a link works after quit: a link carries no typed text or choice, and the board's items don't outlive the host. Ochre clears its notifications at start and quit instead; see the spike results.
  - **Acting** goes through Core's `AttentionActions`: a click names only its notification's session; the module maps that to the item it showed, and the action runs only if that item is still listed and the action fits its kind (`allow`/`deny` a permission, `answer` a question by option index, `approve`/`reject` a plan with text, `send` to a finished agent, `dismiss`). A refused click shows a short notification saying why. Six tests in `AttentionBoardTests`.
  - **In view**: pages report `attention.viewing` (focused, on the inbox, which threads); a session in view in the focused window gets no notification, loses one it had, and its item never pops up later. `AttentionViews` keeps this per window and drops a window when it closes.
  - **Modules** reach the board, the views, the actions and "show this task" through `IModuleContext.Attention` and `ShowTask`; Open brings a window showing the task to the current desktop.
  - **Not built**: per-kind settings. The only kind that doesn't notify is an outside session waiting; a setting can come when dogfooding asks.
  - **Dev runs**: headless records notifications (`RecordingNotifier`) unless `OCHRE_DEV_NOTIFY=1`; `ochre-cli dev notifications` and `dev notify <tag> <action> [key=value]` drive them. Each profile has its own AppUserModelID (`Ochre`, `Ochre.dev`).

### Slice 5: Roost model

- New module project `modules/Ochre.Modules.Roost/` (`roost`, on by default; ADR 0013). Port `RoostGridLayout` and `RoostTemplates` with `RoostGridLayoutTests` and `RoostTemplatesTests`. Commit names Perch, the paths and `9275e0b`.
- `RoostGrid`: one layout plus region-to-session cells and a zoomed region, taken from `RoostTabSet`'s placement rules (assign swaps, unassign, toggle zoom, sync empties a region whose session is gone) with the matching tests from `RoostTabSetTests`; tabs, `Show`, `DropOnTab`, duplication and the layout library stay behind.
- Saved per task in the module's data folder (`roost.json`: layout regions, cells, zoom), written atomically. A task with no saved grid gets `ForCount` for its sessions, filled in start order. A session that joins later goes into an empty region if there is one, else to the "not in the grid" strip; nothing already placed moves.
- Bridge `roost.get {taskId}`, `roost.applyPreset`, `roost.assign`, `roost.unassign`, `roost.zoom`, and a `roost.changed` event so two windows on one task agree. The host builds every layout from a preset name or its own last layout, never from regions the page sends, and checks that every placed session belongs to the task.
- Done 2026-11-26 (no UI yet; checked through the bridge in a headless dev host). Ported from Perch `9275e0b`: `RoostGridLayout` and `RoostTemplates` (`src/Perch.Core/Data/Roost/`) whole, with `RoostGridLayoutTests` and `RoostTemplatesTests`; the JSON round trip now goes through `roost.json`'s own serializer. `RoostGrid` is new, with Perch's single-tab placement rules and its own tests (`RoostGridTests`). Decisions made while building:
  - **"Known" sessions**: the grid remembers every session it has seen, so one the user took out isn't put back by the next sync, and only a session it has never seen fills an empty region. `roost.json` keeps it with the layout, cells and zoom.
  - **Every call returns the whole grid** (`RoostGridDto`: regions with their numbers and sessions, the unplaced sessions, the matching preset, and the presets for the menu), so the page never works out placement itself; `roost.changed` tells other windows to fetch it again.
  - **Aspect** shapes a grid only when it's made (`roost.get` carries the stage's width / height).
  - A grid for a deleted task is dropped when `roost.json` is next read.

### Slice 6: grid view

- Larger visual change: starts from a design artefact the user agrees first (`docs/design/m5-roost/`). Agreed 2026-11-26; see its README.
- A **Grid** title-bar view from the module, its parameter (the task) kept per window, and a **Grid** link beside a task's agents.
- Regions are a CSS grid from the layout's unit spans. Each pane: a header (status dot and word, role, model, context, a menu with Remove from grid and Open thread), then the thread in a compact form of `SessionView`. Controlled panes have the composer and the permission, question and plan cards; dormant ones resume on send; observed ones say "Read-only: started outside Ochre" and have no composer.
- Placement is the user's: drag a pane's header onto another region to swap, click an empty region to choose from the unplaced sessions, choose a preset from a menu (the nine templates). Zoom a pane (button, Ctrl+Shift+Z; Esc restores); Alt+1 to Alt+8 focus regions in reading order; Ctrl+. picks the next pane needing you when one is in the grid.
- A pane on screen in the active window reports `attention.seen`, so a turn finished in view never reaches the inbox's unseen list.
- Check eight streaming panes stay responsive (one agent running a long reply, seven idle; then several streaming at once); if not, panes off-screen or zoomed away drop their thread and show the last lines until shown again.

### Slice 7: exit run

- Walk the exit criterion by hand, write `docs/testing/m5-testing.md`.

### Not in M5

- A grid across tasks; failed CI and PR reviews in the inbox (a GitHub module, roadmap Later).
- Seen and dismissed marks surviving a restart (revisit when dormant sessions need it).
- Notifications on macOS; chimes and phone push (Perch's ntfy).
- Grid tabs, saved layouts, and editing a layout by splitting, merging or dragging dividers (the model supports them; the UI waits).
- Taking over an observed session in Ochre.

## M6 onwards (outline)

Re-plan each milestone in detail when it starts. Notes worth keeping now:

- **M6 to M8** stay as written in the roadmap; too far out to plan usefully.

## Housekeeping before starting

- Work continues on the `rename-to-ochre` branch for now; no per-milestone branches.
- New ADRs expected: 0009 bridge protocol and type sync (after M0); the shell choice goes into ADR 0002 itself.
