# 0013: The attention inbox is kernel; the Roost grid and notifications are modules

- Status: accepted
- Date: 2026-11-26

## Context

M5 adds an attention inbox (everything that needs the user, grouped by task) and a Roost-style grid of a task's agents. ADR 0007 makes every feature beyond the kernel a module, and lists the Roost grid among them; it says nothing about an inbox.

The inbox's items in M5 all come from the kernel: permission prompts, questions, plan approvals and finished turns of controlled sessions, and the waiting status of observed ones. The kernel already shows the same state as "needs you" on the task list and Today. Failed CI and PR reviews would come from a GitHub module that doesn't exist yet (roadmap, Later).

The grid is a different way to look at sessions the kernel already serves; nothing else depends on it.

| Option for the inbox | Verdict |
|---|---|
| A module | Its only input is kernel state the kernel already surfaces as "needs you"; turning it off would leave that state on the task list anyway, so the toggle buys nothing |
| Kernel, like Today | Chosen. Modules add items later through an extension point |

## Decision

- **The inbox is kernel.** Core's `AttentionBoard` derives items from the session manager each time sessions change. It stores no items, only which finished turns the user has seen and which items they dismissed, in memory. The task list, Today and the inbox all read "needs you" from it.
- **Modules add items later** through a `Signal` extension point (ADR 0007's list), added with the first module that needs one (a GitHub module for CI). Until then items have no source field beyond their session.
- **The Roost grid is the `roost` module**, on by default: a title-bar view whose parameter is a task, and its layouts saved per task in the module's data folder. The host builds every layout from a preset or the saved one and checks every placed session belongs to the task; the page never sends regions.
- **Desktop notifications are the `notifications` module**, on by default, a consumer of the attention board like the inbox view. Showing them is an OS capability behind a Core interface (`IDesktopNotifier`) with a Windows implementation; how that implementation is built is settled by the M5 notifications spike. A notification's action names only an item and an action, and the host acts only on an item that is still current.

## Consequences

- With every module off, the inbox still works; with `roost` off, a task's agents are still reached from the task and the inbox; with `notifications` off, nothing pops up outside Ochre.
- Seen and dismissed marks don't survive a restart. Sessions come back dormant after one, so their finished turns aren't raised again; a pending permission can't outlive its process either.
- A GitHub module will need the `Signal` contract designed for it, and the inbox's item record will gain a source.
