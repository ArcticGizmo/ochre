# M2 slice 3: Today and the UI design pass

What changed: the window opens on **Today**, a new screen listing every Active and Parked task by last activity, each with its state, where you left off (your note, then each agent's line), the next unticked step with progress, its agents and its links. Clicking an agent opens its thread with the composer focused, so a dormant agent is one message from resuming; its own line from the park note sits above the composer. The title bar has Today and Tasks tabs. Tasks has a search box, counts on every filter, and a List/Board toggle; the board has one column per state. The task detail has the ID under the title, a coloured status dropdown (Parked opens the Park dialog, Done asks first) and Delete in a ⋯ menu, and is centred. Each state has one colour everywhere; agent statuses read working / ready / needs you / dormant / ended. The design is in `docs/design/m2-today/`.

## Checks

| Try | Before | After |
|---|---|---|
| Start Ochre | Task list with nothing selected | Today: date, Active and Parked counts, one card per Active or Parked task, most recent first |
| Look at a parked task's card | | "Where you left off · parked <time>", your note, then each agent's line with its role in bold; Next step, n / m and a progress bar; agent chips (hollow dot, "dormant"); Resume task |
| Click a dormant agent's chip | Only reachable through the task, then Open | Its thread, "← Today", status DORMANT, that agent's park note above the composer, composer focused; typing and Resume and send resumes it |
| Click ← Today | | Back on Today |
| Click Resume task on a parked card | | The card shows Active; the note stays |
| Click a card's title | | Tasks, list view, that task selected |
| Tasks: type part of a title or an id in Search | No search | The list and every filter count narrow to matches; Esc clears |
| Look at the filter chips | Open / Active / ... with no counts | Each shows its count, e.g. Active 2 |
| Click Board | No board | Four columns (Backlog, Active, Parked, Done) with counts, cards sorted by last activity; Done shows the latest five and "Show N more"; a narrow window scrolls sideways |
| Click a board card | | List view with that task selected |
| Task detail header | ID beside the title, state chips under it, Delete link | Title, then `T-nnnn · created … · updated …` under it; status pill in the state's colour; Park or Resume; ⋯ |
| Open the status pill, pick Active or Backlog | | State changes at once |
| Pick Parked… | | The Park dialog, as from the Park button |
| Pick Done… | Changed at once | "Mark T-nnnn done?" confirm; Cancel leaves it as it was |
| ⋯ → Delete task… | | The existing delete confirm |
| Agents on the task detail | BUSY / DORMANT / not running | Dot and word: working, ready, needs you, dormant, ended; Open for any but ended |
| An agent removed from the ledger (shows ended) | | Its Today chip is greyed and can't be clicked |
| Two windows, one on Today, one on Tasks | | Each keeps its own view; ticking a step in one updates Today in the other |

## After the first hands-on review

Board moved to the title bar (Today, Tasks, Board); New task is a + beside the search box. The thread uses bubbles after Perch: yours ochre-tinted on the right, the agent's raised on the left, with tool calls as cards inside it. The thread header has model and effort pickers; the cost moved into the context figure's tooltip. A task in the list expands to show its agents and what each is doing, with running subagents under them.

| Try | Before | After |
|---|---|---|
| Title bar | Today, Tasks; List/Board inside the Tasks pane | Today, Tasks, Board |
| Tasks pane top | Wide New task button, List/Board, search below | Search with a + beside it (tooltip "New task") |
| Open an agent's thread | Agent text on the background, your text in a grey box | Agent replies in raised bubbles on the left, yours in ochre-tinted bubbles on the right; tool calls as cards in the agent's bubble; "working…" as a bubble |
| Thread header | model, mode, $ cost, context | Model picker (Fable 5.2, Opus 5.5, Sonnet 5.5, Haiku 5.5) and effort picker (auto … max) top right; meta shows the mode and context; hover the context for the API-equivalent cost |
| Pick a model on a running agent | | A note "model → …" in the thread; the next reply uses it |
| Pick an effort on a dormant agent, then send | | Note "effort → …"; it resumes with `--effort`; the pick survives restarting Ochre |
| Tasks list, a task with a running agent | | Expanded: one row per agent with a dot and what it's doing (the running tool, thinking, writing), or its status word when idle; running subagents (violet dots) under it |
| Click the chevron | | Collapses or expands; a collapsed task whose agent waits on you shows "needs you" |
| Click an agent row | | That task selected and the agent's thread open; the row highlighted |
| Ask an agent to use a subagent | | The subagent's description appears under the agent while it runs, and goes when it finishes |

## Automated

None new; the 275 existing tests pass. The list item now carries the task's sessions, links and park time; the session summary and header carry the agent's activity and subagents; the ledger keeps a picked model and effort. Checked by hand in the dev window.
