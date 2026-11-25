# M2 slice 1: dormant sessions and resume

What changed: agents survive a restart of Ochre. Every agent a task launched comes back DORMANT; opening one loads its thread from the Claude transcript (active branch only, last 4000 lines). Sending to a dormant agent resumes the same Claude session (`--resume <id>`), so it keeps its context, its task binding and `OCHRE_SESSION`. A small ledger (`%LOCALAPPDATA%\Ochre\sessions.json`) remembers each agent's permission mode and, after a `/clear`, which Claude session to resume.

## Checks

| Try | Before | After |
|---|---|---|
| Start an agent on a task, let it tick a step, quit Ochre, relaunch, open the task | The agent showed NOT RUNNING with no way in | It shows DORMANT with Open |
| Open it | | The thread shows the earlier conversation (your message, tool cards, its reply); the composer says sending resumes it; the button reads "Resume and send" |
| Send "Which plan items did you tick earlier? Then untick p1 with the task uncheck command." | | A "resumed" note; the reply names the items it ticked before the restart; p1 unticks; the log says `session <same id> unchecked p1` |
| Start an agent with "Plan first" and no first message, quit, relaunch, open it | | "no conversation recorded yet; sending starts it". Sending shows "started", and the header says `plan`: the mode came back from the ledger |
| Stop a running agent (confirm) | It ended for good | It goes DORMANT; sending resumes it |
| Open the same session in a terminal (`claude --resume <id>`), then send to it in Ochre | Two writers on one transcript | Error: open in another Claude Code (process N); close it there first |
| Delete or rename the agent's folder, then send | | Error naming the missing folder |
| Look at `%LOCALAPPDATA%\Ochre-dev\sessions.json` | | One entry per agent launched since this change: id, task, cwd, role, mode, claudeId. No prompts or output |

Agents launched before this change come back too (from the task's `sessions` list), with the default permission mode.
