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

| Don't test (yet) | Why |
|---|---|
| The M0 spike | Throwaway. |
| UI, bridge plumbing, tray, window management | Changing fast; verified by hand through the manual testing docs. |
| CLI argument handling, pipe transport | Thin; exercised by every dogfooding session. |
| "Kernel with every module off" (ADR 0007) | Deferred to M4, when the module contracts exist and settle. |

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

- Bridge v1 from the spike's findings: `tasks.list`, `tasks.get`, `tasks.create`, `tasks.update` (title, repos, notes), `plan.check`, `plan.add`, and a `task.changed` event.
- List with state filter and manual ordering (an `order` frontmatter field). Detail view: plan checkboxes, notes (markdown rendered with DOMPurify), links shown read-only, sessions list, log.
- Edits from the UI go through the store like any other writer, with author `user`.

### Slice 4: controlled sessions (host side)

- Port from Perch: `ClaudeSessionController`, `StreamJsonParser`, `ControlProtocol` (the parts that apply), `SessionEvents`, `SessionConversation`, `ControlledSessions`, plus `ClaudeConfigDir`, `ClaudePaths` and `DirectoryTrust`. Bring their tests and fixtures. Commit message names Perch, the paths and `9275e0b`.
- "Start agent" on a task: choose a repo from the task's `repos` (or a folder), optional role, and launch with `OCHRE_TASK` and `OCHRE_SESSION` in the environment and the Ochre CLI's folder on `PATH`.
- Tell the agent about the task with `--append-system-prompt` (decided): a short paragraph explaining `ochre task show`, `check`, `add-step` and `note`. Without this the agents won't use the CLI, and the task isn't load-bearing.
- Record the session in the task's `sessions` list with `binding: launched`.
- Respect folder trust (`DirectoryTrust`) before launching, as Claude does.

### Slice 5: session thread view

- Render `SessionConversation` in the web UI: user and assistant messages (streaming markdown, sanitised), tool calls folded into cards, a composer, and stop/interrupt.
- Permission prompts, `AskUserQuestion` and plan approval cards, since a controlled session is stuck without them. Port the input models (`AskUserQuestionInput`, `PlanApprovalInput`, `SessionAttention`) and use Perch's UI as the behavioural spec.
- Defer: slash-command palette, rewind, images, search. Sessions stay in memory only; whether they survive quit is M2's dormant-session work.

### Slice 6: agent CLI

- `src/Ochre.Cli`, NativeAOT, a few hundred lines: parse args, send one JSON line over the per-user pipe, print the reply. Commands: `task current`, `show [--plan]`, `check`, `uncheck`, `add-step`, `note`.
- Host command handler enforces the rules: the session id from `OCHRE_SESSION` must be bound to the target task; agents cannot change `state` or remove links; every write logged with the session id.
- Optional hardening, cheap with named pipes: resolve the client pid (`GetNamedPipeClientProcessId`) and check it descends from a Claude process Ochre launched, so a stray process can't impersonate a session by setting env vars. Same-user processes are inside the trust boundary anyway, so this is a nice-to-have.
- UI highlights plan items added or changed by agents until the user acknowledges them.
- **Tests:** the write rules only.

### Slice 7: exit run

- Walk the exit criterion by hand, write `docs/testing/m1-testing.md`, then start tracking Ochre work as Ochre tasks.
- Optional cheap win to try while dogfooding: a manual "park note" field the user fills in by hand. It previews M2's value before the agent-driven version exists.

### M1 risks

| Risk | Mitigation |
|---|---|
| Agents ignore the CLI | Prompt via `--append-system-prompt`; watch it while dogfooding; MCP in M2 may be more reliable than shelling out |
| User edits a task in an editor while an agent writes | Read-modify-write under the per-task lock reloads first, so the window is small; accept last writer wins for now |
| Thread view takes longer than expected | The conversation model, stream parsing and input models come over from Perch as-is, so only the web rendering is new. Ship text, tool cards and permission prompts first; everything else waits |
| EDR flags the host spawning Claude and shells | Sign dev builds early if it bites; report blocks, never work around them |

## M2 onwards (outline)

Re-plan each milestone in detail when it starts. Notes worth keeping now:

- **M2: park, resume, Today, observed sessions, MCP.** Port `SessionEnd`, `SessionLedger`, `RecentSessions` and the transcript readers; check Perch's `session-recovery` branch first. Park sends each running session a "write your park note" turn that calls `ochre task park-note`. Dormant sessions resume with `--resume <id>` on first send. MCP server shares the CLI's command handler. Binding suggestions only ever suggest. Tests: transcript readers come with Perch's; nothing new unless park-note collection proves fragile.
- **M3: terminal tabs.** The spike's ConPTY and throughput findings feed directly in. Scrollback encrypted with DPAPI; OSC 52 and OSC 8 gating (security threat 7). Test the escape-sequence filter, since it's a security boundary.
- **M4: module host.** This is where the contracts from ADR 0007 get designed, using the real features built in M1 to M3 as the examples. Move terminal, git and others behind modules, then add the "every module off" kernel test. Git graph, Jira, sprig and multi-window follow.
- **M5 to M8** stay as written in the roadmap; too far out to plan usefully.

## Housekeeping before starting

- Work continues on the `rename-to-ochre` branch for now; no per-milestone branches.
- New ADRs expected: 0009 bridge protocol and type sync (after M0); the shell choice goes into ADR 0002 itself.
