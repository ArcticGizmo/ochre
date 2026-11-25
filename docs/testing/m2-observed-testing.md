# M2 slice 4: sessions started outside Ochre

What changed: Ochre now sees Claude Code sessions you started yourself (in a terminal, the desktop app, another tool), from Claude's own session files, without a hook (ADR 0006). Today lists the ones no task has under "Other Claude sessions", with their folder, branch and status, and suggests tasks they look like they belong to: the folder is inside one of the task's repos, or the branch names a Jira issue the task links to. One click links a session to a task (`binding: confirmed`); a task also shows a bar for each outside session that looks related. Opening one shows its thread read-only, following its transcript as it grows. Linked sessions appear among the task's agents tagged "outside Ochre", with Unlink. Ochre never sends to, stops or parks them, and they can't use the task's agent commands.

## Checks

| Try | Before | After |
|---|---|---|
| Start `claude` in a terminal in one of a task's repos, then look at Today | Nothing about it | "Other Claude sessions" lists it: folder name, status (working / ready / needs you), path, branch; a dashed "Link to T-nnnn … · folder is in <repo>" for each matching task |
| Work on a branch like `feature/PROJ-142-x` where a task has a Jira link to PROJ-142 | | Suggested with "branch names PROJ-142", ranked above a folder-only match |
| Click the session's chip | | Its thread, read-only: "outside Ochre" tag, branch in the meta, no model/effort pickers, no Stop, no composer; a note says to carry on where it's running |
| Type in that terminal session while its thread is open | | New messages and tool calls appear within a couple of seconds; "working…" while it's busy |
| Click a suggestion (or Link to… and pick a task) | | It leaves "Other Claude sessions"; the task's card on Today shows its chip; the task log has `user linked session <id>`; the file has `binding: confirmed` |
| Open the task | | A blue bar for any other outside session that looks related, with Open and Link to this task; the linked one in Agents with OUTSIDE OCHRE and Unlink |
| Park the task | | The Park dialog doesn't list the outside session; it isn't asked for a note or stopped |
| Close the terminal session | | Unlinked: it disappears from Today. Linked: its status turns "closed"; its thread still opens |
| Restart Ochre | | A linked, closed session is still on the task and its thread opens |
| Click Unlink | | Gone from the task; the log has `user unlinked session <id>` |
| From inside a linked outside session, set `OCHRE_SESSION` and `OCHRE_TASK` by hand and run `ochre-cli task check p1` | | `error: this session was started outside Ochre and only linked to T-nnnn, so it can't use the task…` |
| Run this with an agent Ochre launched | | It's never listed as an outside session |

## Automated

`TranscriptTailReaderTests` and `JiraTicketTests` came over from Perch (`9275e0b`). `BindSuggestionsTests` covers the folder and Jira rules. `AgentCommandsTests` covers the read-only rule for linked sessions (security boundary). `SessionLifecycleTests` drives outside sessions with fake session files and transcripts: listed read-only with a suggestion, linking and outliving the process, a restart, unlinking, the thread following its transcript and picking up the branch, and a launched agent never being taken for an outside one.
