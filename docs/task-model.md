# Task model

## Storage

- One markdown file per task under the app data dir (`%APPDATA%\Nidus\tasks\T-0042.md`). App-local by decision ([ADR 0001](decisions/0001-app-local-tasks.md)): a task can span several repos.
- **The host is the only writer.** Agents and UI change tasks through the host (CLI/MCP/bridge), which serialises writes, so two agents never race on one file. External edits (you, in an editor) are picked up by a file watcher and merged on the next read.
- Writes are atomic (write temp, then rename) and every change is appended to the task's log with its author.

## File format

```markdown
---
id: T-0042
title: Usage readout for multi-org accounts
state: active            # backlog | active | parked | done
created: 2026-11-25T09:00:00Z
updated: 2026-11-25T16:40:00Z
repos:
  - C:\git\example-app
  - C:\git\example-lib
links:
  - id: l1
    kind: jira
    ref: PROJ-1234
    title: Usage bar wrong for second org
    summary: Monthly spend reads the primary org's value for every account.
  - id: l2
    kind: pr
    ref: example-org/example-app#120
  - id: l3
    kind: task
    ref: T-0039
sessions:
  - id: 4a7cc5b2-…           # Claude session id
    cwd: C:\git\example-app
    configDir: C:\Users\me\.claude
    binding: launched        # launched | confirmed
    role: implement
park:
  at: 2026-11-24T17:55:00Z
  note: |
    Parser fix done and tested. Next: wire the per-account bar in the overlay, then run the multi-org fixture.
---

## Plan
- [x] Reproduce with the two-org fixture {#p1}
- [x] Fix the spend parser unit {#p2}
- [ ] Per-account bar {#p3}
  - [ ] Colour by pace {#p3a}
- [ ] Manual test doc {#p4}

## Notes
Free-form notes, decisions, gotchas.

## Log
- 2026-11-24T15:10Z session 4a7cc5b2 checked p2
- 2026-11-24T17:55Z parked by user
```

- Plan items carry **stable ids** (`{#p3}`) so agents address items by id, not by position.
- `links` is the **data-source hub**: kind + ref + a short title and summary. Detail is fetched on demand through the module that owns the link kind (`LinkResolver`).

## Lifecycle

```
backlog ──start──▶ active ──park──▶ parked ──resume──▶ active ──done──▶ done
                     ▲                                                 │
                     └──────────────────── reopen ─────────────────────┘
```

- **Done is a human decision.** Agents finishing their turns never completes a task.
- **Park** (manual, or on OS shutdown when enabled): each bound, running session is asked for a "state + next step" note, which is stored in `park.note`. Sessions then go dormant (they show their conversation and resume when you send something).
- **Resume**: the task opens with its park note, plan and dormant sessions. One click wakes a session.

## The morning view ("Today")

For every Active and Parked task: title, park note, next unchecked plan item, attached sessions (running / dormant / ended), worktree and git state, links. Sorted by last activity. This is the screen that answers "where do I start?".

## Agent surface

Agents reach their task through the `nidus` CLI (and an MCP server with the same operations). A session launched from a task gets `NIDUS_TASK=T-0042` and `NIDUS_SESSION=<id>` in its environment.

| Command | Effect |
|---|---|
| `nidus task current` | The task id for this session (env, else binding lookup) |
| `nidus task show [--plan]` | Title, state, plan, notes, park note |
| `nidus task links` | The links index (kind, ref, title, summary) |
| `nidus task fetch <link-id>` | Detail for one link through its resolver (marked as untrusted content) |
| `nidus task check <item-id>` / `uncheck` | Tick a plan item |
| `nidus task add-step "<text>" [--under <item-id>]` | Add a plan item |
| `nidus task note "<text>"` | Append to notes |
| `nidus task park-note "<text>"` | Write this session's state + next step (used during park) |

Rules:
- An agent can only write to the task it is bound to.
- Agents cannot change `state` (no marking done) or remove links.
- Every write is logged with the session id, and plan changes by agents are highlighted for review in the UI.

## Session binding

- **Launched** from a task: bound automatically (`binding: launched`).
- **Observed** sessions (started elsewhere): Nidus **suggests** a binding from cwd, worktree, branch name, or a Jira key in the branch, and you confirm with one click (`binding: confirmed`). It never binds silently.
- A session can be bound to one task at a time. Rebinding is explicit.
