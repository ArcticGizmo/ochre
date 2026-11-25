# M2 slice 2: park and resume a task

What changed: a task can be parked. Park opens a dialog naming the task's running agents and taking an optional note of your own. Confirming sets the task Parked with your note straight away, asks each running agent for its state and next step (it answers with `ochre task park-note`, which runs without a prompt but is only accepted while the park is open, once per agent), then stops the agents so they wait dormant. A busy agent gets two minutes to finish its turn before it is interrupted; an agent that never answers is stopped after two more. The note in the task file is your note first, then one line per agent: `- alpha: ...`. Resume sets the task Active; the note stays above the plan until the next park.

## Checks

| Try | Before | After |
|---|---|---|
| On an active task with one running agent, click Park | No way to park except picking the state chip | A dialog: the task moves to Parked, its running agent (by role) will be asked where it is and what comes next, then stopped; a note field |
| Type a note, click Park task | | The task shows PARKED at once; "Where you left off" shows your note and "Waiting for park notes from alpha..." |
| Wait | | The agent's line appears (`- alpha: ...`), the waiting line goes, the agent shows DORMANT; the log has `user parked` and `session <id> wrote a park note` |
| Park a task with two running agents | | Both are asked; both lines appear in either order; both go dormant |
| Park while an agent is mid-turn | | It finishes (or after two minutes is interrupted), then is asked |
| Park a task with no running agents | | Parked with just your note; nothing waits |
| Click Resume on the parked task | | Task Active; the note stays above the plan; agents stay dormant until you send to one |
| Send to a dormant agent after resuming | | It resumes with its conversation and knows where it was |
| From inside an agent when nothing is being parked: `ochre-cli task park-note "x"` | | `error: a park note is only taken while the user is parking the task...` |
| Ask an agent to write a second park note during the same park | | `error: this agent's park note has already been written` |
| `task park-note` with several lines of text | | Saved as one line |

## Automated

`AgentCommandsTests` cover the park-note rules (only while open, once, bound sessions only, folded to one line). `SessionLifecycleTests` drive parking with an in-memory claude: no agents, two agents answering, and a busy agent that is interrupted and then never answers.
