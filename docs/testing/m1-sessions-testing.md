# M1 slices 4 to 6: agents from tasks

What changed: a task can start Claude agents. Each one runs as a controlled session in the host (stream-json, ported from Perch), is recorded on the task (`sessions:` with `binding: launched`), and gets `OCHRE_TASK`, `OCHRE_SESSION` and the Ochre CLI's folder on `PATH`, plus an appended system prompt that explains `ochre task show / check / add-step / note`. The window shows each agent's thread with tool cards, permission, question and plan-approval cards, a composer, Interrupt and Stop. Agents can only reach the task they were started from; every write they make is logged as `session <id>`.

## Setup

1. Run `run.cmd` (builds the CLI, UI and host, then starts the dev host). Set `OCHRE_DEV_DESKTOP=<virtual desktop guid>` first if the window should open on another desktop.
2. Have a task with at least one repo and two plan steps.
3. Claude Code must be installed and signed in (`claude` on `PATH`).

## Checks

| Try | Before | After |
|---|---|---|
| On a task, click "Start agent" | The Agents section only listed ids | A dialog with Folder (the task's repos, or another folder), Role, Permissions and a pre-filled First message |
| Start with role `checker`, permissions "Ask before edits and commands", first message: "Run the task show command, tick both plan items with task check, then reply done. Don't edit files." | No way to launch an agent | The thread opens; your message shows; the header says CHECKER, BUSY, the folder, model and mode |
| Wait for the agent's commands | Each `ochre-cli task ...` waited on an "Allow PowerShell?" card | No prompt for the task commands (ADR 0010): tool cards run `ochre-cli task show` and `task check` (`ochre task ...` when installed) and get green dots; expanding one shows what the CLI printed |
| Watch the task's plan (Back to the task) | | Both steps are ticked; the log has `session dc59ad95 checked p1` style lines, not `user` |
| Ask the agent to run `git status` | | A yellow-bordered "Allow PowerShell?" card; status WAITING. Allow turns it into a one-line "allowed" receipt |
| Ask it to run `git log -1` and click Deny | | The card reads "declined"; the agent says it was denied |
| Send "what is 2+2?" from the composer, Enter | | Your message appears, the reply streams in as plain text, then renders as markdown when done; status returns to IDLE |
| Send a message while it's still answering | | "1 queued" shows by Send until the turn ends |
| Ask it something long, click Interrupt | | The turn stops; status IDLE |
| Start an agent with "Plan first" and ask for a small change | | A "Plan ready" card with the plan rendered, and Keep planning / Approve, acceptEdits / Approve |
| Ask the agent to ask you a multiple-choice question with AskUserQuestion | | A question card with the options; Answer is enabled once each question has a choice; the receipt shows "Header: choice" |
| Start an agent in a folder Claude Code has never trusted (e.g. a new empty folder) | Claude would ask in a terminal; print mode skips it silently | Ochre asks "Trust this folder?"; Trust and start launches; next time it doesn't ask; `.claude.json` has `hasTrustDialogAccepted: true` for that folder |
| Click Stop, confirm | | The session ends with "session ended"; the composer goes; the task's Agents list shows ENDED |
| Close the window and reopen it from the tray while an agent works | | The agent kept running; opening it shows the whole thread so far |
| Open a second window (if available) on the same agent | | Both update together |
| Quit Ochre with an agent running, relaunch | | The task still lists the agent as "not running" (resume comes with M2's dormant sessions) |
| From a normal terminal, run `ochre-cli task show` | | `error: not running in an Ochre agent session (OCHRE_SESSION is not set)` |
| In a terminal, set `OCHRE_SESSION` to a random GUID and `OCHRE_TASK` to the task, then `ochre-cli task check p1` | | `error: this session isn't bound to T-...`; the file is unchanged |
| `ochre-cli task set-state done` from inside an agent | | `error: unknown or malformed command`: agents have no way to change state |

## Automated

`dotnet test` covers the ported Perch pieces (stream-json parser and replays, conversation model, folder trust, executable resolution) and the agent write rules (`AgentCommandsTests`: bound task only, logged per session, no state change, unbinding takes effect at once, the system prompt stays command-line safe).

## Known gaps

- Sessions live in memory only; after a quit they show as "not running". Dormant sessions and resume are M2.
- `/clear` inside an agent gives it a new Claude session id, but its `OCHRE_SESSION` (and the task's record) keep the launch id, which is what the binding check uses.
- No composer palette, images, rewind or search yet.
