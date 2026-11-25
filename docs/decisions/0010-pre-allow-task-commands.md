# 0010: Agents Ochre starts may run the task commands without a permission prompt

- Status: accepted
- Date: 2026-11-25

## Context

Agents launched from a task keep its plan current through `ochre task show / check / uncheck / add-step / note` (task-model.md, "Agent surface"). Each of those is a shell command, so under Claude Code's normal permission modes every one waits on an "Allow PowerShell?" card. In the first hands-on run an agent stopped at its very first `task show`, and two agents ticking four items would have needed at least six clicks. Agents that wait for a click on every tick stop keeping the plan current, which removes the reason the task exists.

The commands themselves are narrow. The host, not the agent, enforces what they can do (security.md threat 3, `AgentCommands`): only the task the session is bound to, checked against the file inside each write; no command changes state or removes anything; every write is logged as `session <id>`, and agent plan changes are highlighted until the user marks them reviewed.

Options considered:

| Option | Verdict |
|---|---|
| Keep asking for every task command | Safe but defeats the feature; agents stall on the first tick |
| A per-launch checkbox, default off | One more decision on every launch, for commands whose limits don't change between launches |
| Pre-allow only the task commands, at launch | Chosen |

## Decision

When the host starts an agent it passes `--allowedTools "Bash(<cli> task *)" "PowerShell(<cli> task *)"`, where `<cli>` is the CLI's file name (`ochre`, or `ochre-cli` in a dev checkout). Nothing else is pre-allowed: edits, other commands and every other tool still follow the permission mode the user picked. The rules come from `AgentPrompt.AllowedTools` and are held to a closed character set before they reach the command line.

## Consequences

- Agents tick, add steps and write notes without interrupting the user; the review bar is where the user looks at what they did.
- A prompt-injected agent can scribble on its own task's plan and notes without asking. That was already possible after one Allow click; the mitigations are the binding check, the log and the review highlight, not the prompt.
- Claude Code's own prefix matching decides what counts as a task command, so a compound command (`ochre task show && something`) still asks for the part that isn't one.
- If agents ever gain a task command with a wider effect (`task park-note` is next, in M2), it inherits this rule, so its limits must be enforced on the host like the others.
