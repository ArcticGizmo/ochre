# M1 exit: tasks and controlled sessions

The roadmap's exit criterion for M1: create a task, launch two agents on it, both tick plan items, quit and relaunch, and the task is intact. Slice 7 also added the review highlight for agent changes. The per-slice checks are in `m1-skeleton-testing.md`, `m1-tasks-ui-testing.md` and `m1-sessions-testing.md`.

## Exit run

Walked on 2026-11-25 against the dev profile (task T-0008), with Claude Code 2.1.332.

| Try | Expected | Seen |
|---|---|---|
| New task "M1 exit run: two agents on one task", repo set, four steps: "Agent A: tick this", "Agent A: tick this too", "Agent B: tick this", "Agent B: tick this too" | Task with p1 to p4 | As expected |
| Start agent, role `alpha`, "Ask before edits and commands", first message: "Run the task show command, then tick only the plan items whose text starts with Agent A, using the task check command, one at a time. Then reply done. Do not edit or create any files and run no other commands." | Thread opens, agent busy | As expected |
| Back to the task, start a second agent, role `beta`, same message with "Agent B" | Both listed under Agents, BUSY | As expected |
| Wait about 30 seconds | No permission cards for the task commands; both agents go IDLE; all four steps ticked; list shows 4/4 | As expected |
| Look at the log | `session 5e8c2a0c checked p1`, `... p2`, `session 965ebaec checked p3`, `... p4`: each write under its own session | As expected |
| Look at the plan | A yellow "Agents changed 4 plan items since you last looked" bar with Mark reviewed; each item has an AGENT tag | As expected |
| Note the file's hash, `cli quit`, `run.cmd -NoBuild` | No agent `claude` processes left behind; the file is byte-identical; the list shows 4/4; both agents show NOT RUNNING | As expected |
| Click Mark reviewed | Bar and tags go; the log gains `user reviewed agent changes` | As expected |

## Review highlight

| Try | Before | After |
|---|---|---|
| An agent ticks or adds a step | Only the log showed it | The step has an AGENT tag and the review bar counts it |
| An agent adds a note (`task note`) | | The Notes heading has an AGENT tag and the bar mentions the notes |
| Mark reviewed | | Tags and bar clear; one `user reviewed agent changes` log line. Nothing new to review writes nothing |
| An agent ticks another step after the review | | Only that step is tagged |
| Delete a tagged step by hand in an editor | | It drops out of the count |

The review state is worked out from the log (agent entries since the last review), so it needs no fields of its own and survives hand edits.

## Not covered by M1

Sessions don't survive a quit: after relaunch the task lists them as NOT RUNNING. Parking, dormant sessions and resume are M2.
