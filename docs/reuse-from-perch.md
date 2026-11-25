# Reuse from Perch

Nidus copies code from Perch where it helps ([ADR 0003](decisions/0003-separate-repo-copy-and-own.md)). Copied code is owned here and may diverge. When copying, note the source path and Perch commit in the commit message so fixes can be compared later.

Reference point: Perch `main` at `9275e0b` (2026-11-25). Paths are relative to the Perch repo.

## Strong candidates (UI-free logic in `src/Perch.Core`)

| Area | Perch source | Use in Nidus |
|---|---|---|
| Controlled sessions | `Data/Control/ClaudeSessionController.cs`, `StreamJsonParser.cs`, `ControlProtocol.cs`, `SessionEvents.cs`, `SessionConversation.cs`, `ControlledSessions.cs` | Launch and drive Claude Code over stream-json (M1) |
| Interaction inputs | `Data/Control/AskUserQuestionInput.cs`, `PlanApprovalInput.cs`, `SessionAttention.cs` | Question and plan cards, attention states |
| Rewind / resume | `Data/Control/RewindPoints.cs`, `ResumeEstimate.cs` | Later |
| Slash commands, skills | `Data/Control/SlashCommandCatalog.cs`, `SkillCatalog.cs` | Composer palette |
| Tool output | `Data/Control/EditDiff.cs`, `ToolResultFormat.cs` | Tool cards in the thread view |
| Transcripts | `Data/TranscriptReader.cs`, `TranscriptScan.cs`, `TranscriptJson.cs`, `TranscriptLocator.cs`, `TranscriptChain.cs`, `TranscriptFold.cs`, `TranscriptTailReader.cs`, `TranscriptLineTail.cs`, `TranscriptSearch.cs` | Observed sessions, history, search (M2) |
| Session end / recovery | `Data/SessionEnd.cs`, `SessionLedger.cs`, `RecentSessions.cs` | Park/resume, abrupt-close and shutdown detection (M2) |
| Config dirs / accounts | `Data/ClaudeConfigDir.cs`, `ClaudePaths.cs`, `SessionAccounts.cs` | Multi-account support |
| Folder trust | `Data/DirectoryTrust.cs` | Replicate Claude's trust gate before launching |
| Pricing | `Data/ModelPricing.cs` | Per-task cost (later) |
| Git | `Data/GitRunner.cs`, `GitWorktrees.cs`, `WorktreeLayout.cs`, `SessionWorktree.cs`, `PrWorktree.cs` | All git calls; worktree discovery (M4) |
| Git graph | `Data/GitGraph/*` (`GraphLoader`, `GraphLogReader`, `LaneAllocator`, `GraphLanes`, `GraphModels`, `BranchVisibility`, …) | Graph model; re-render in the web UI (M4) |
| sprig | `Data/Sprig/*` (`SprigCli`, `SprigStatus`, `SprigStatusFold`, `SprigSessionLink`, `SprigNotices`) | sprig module (M4) |
| Jira | `Data/JiraTicket.cs` | Jira key detection in branches |
| Windows tooling | `Data/CmdShim.cs` | Running `.cmd` tools safely |
| Images | `Data/ImageHeader.cs` | Header-first size checks before decoding |
| Statusline | `Statusline/*` (`StatuslineTemplate`, `StatuslineTokens`, `StatuslineScript`, …) | Declarative widget template language (M7) |
| Placement | `Data/PlacementMath.cs` | Per-monitor window placement (M4) |

Bring the matching tests and fixtures from `tests/Perch.Tests` with each piece.

## On other Perch branches

| Branch | What | Use |
|---|---|---|
| `pluggability-plan` | `src/Perch.Core/Plugins/*`: manifest parser, NDJSON protocol, capability gate, consent, installer (SHA-256 + zip-slip guard), Job Object sandbox | Plugin host (M7) |
| `session-recovery` | Dormant sessions, shutdown clock, power history | Park/resume (M2), if not already on main |

## Not reusable as code

Perch's Avalonia UI (owner-drawn overlay, `SessionWindow`, thread view, Roost, git graph view) won't carry over to a web UI. Use it as the behavioural spec, especially the hard-won fixes (scrolling, streaming markdown, tool folding, sticky card headers).

## Lessons to carry over

- Open live files with `FileShare.ReadWrite` and tolerate partial trailing lines.
- Read context occupancy from per-message usage, not the `result` aggregate.
- Spawned `gh`/git children need redirected stdin and UTF-8.
- `~/.claude/.gitignore` is `*`, so directory greps silently skip files.
- Settings files: atomic save, salvage-merge on corruption, and tests must disable persistence.
