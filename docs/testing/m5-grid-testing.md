# M5 slice 6: grid view

The **Grid** title-bar view (from the `roost` module) shows one task's agents side by side, each pane with its live thread, its prompts and a composer. The design is `docs/design/m5-roost/`. Start the dev host headless (`docs/handover/m5-start.md`) or try it in a normal run; a task with two or more agents makes most rows below visible. T-0013 in the dev profile has two.

| Try | Before | After |
|---|---|---|
| Open a task with agents | Agents heading on its own | A **Grid** button beside the Agents heading (only when the task has agents) |
| Click **Grid** | No such view | The Grid view in this window, on that task: its title (click to open the task), id and state; the panes laid out for the agents; the Layout menu and the keys |
| Click **Grid** in the title bar with no task chosen in this window | | A list of open tasks with agents to choose from |
| Open the task switcher (the chevron after the state) | | The other open tasks with agents; picking one shows its grid in this window only |
| Look at a pane | | Its number, status dot and word, name (an outside session's folder in mono with "outside Ochre"), model and context (or an outside session's branch), zoom and ⋯ buttons; the thread in a tighter form; a one-line composer |
| A dormant agent's pane | | The composer says "Sending resumes … with its conversation" and its button **Resume and send**; sending resumes it in place |
| An outside session's pane | | "Read-only: started outside Ochre. Carry on with it where it's running." and no composer |
| Ask an agent for a Write in its pane | | The pane's border turns amber, the word reads "needs you", and the Allow/Deny card shows in the pane. It doesn't take focus from a composer you're typing in |
| **Layout** menu | | Nine thumbnails in three rows, the current one in the accent; picking one lays the grid out again, agents keep their order, and ones in regions the new layout lacks go to **Not in the grid** |
| An empty region | | "Empty", the agents not in the grid first, then the placed ones ("in 2 · moves here"); clicking one puts it there |
| Drag a pane's header onto another pane | | The target shows a dashed accent border while dragging over it; dropping swaps the two |
| Drag a pane's header onto an empty region, or a chip from **Not in the grid** onto a pane | | It moves there; a pane it lands on goes to Not in the grid |
| Click a **Not in the grid** chip | | A menu of regions ("Into 1, empty", "Into 3, in place of …") |
| ⋯ → **Remove from grid** | | The pane leaves; its agent is a chip in Not in the grid; a new agent started later still fills an empty region |
| ⋯ → **Open thread** | | The agent's full thread beside its task (Tasks view) |
| **Alt+1** to **Alt+8** | | Focus that region: a pending prompt's main button, else its composer, else (empty region) its first choice |
| **Ctrl+Shift+Z** with focus in a pane, or its zoom button | | The pane fills the grid with its thread kept to a readable width; the toolbar says "Zoomed: 2 of 4", has **Restore**, "Esc restores" and the other agents as small chips (click one to zoom it instead). A draft typed in another pane is still there after restoring |
| **Esc** while zoomed (no menu or dialog open) | | The grid restores and focus stays in the pane |
| **Ctrl+.** with a pane needing you | Ctrl+. jumps to the inbox's next item | Focus moves to the next pane needing you (reading order, wrapping), switching the zoom if another pane is zoomed |
| **Ctrl+.** with no pane needing you | | As before: the inbox's next item, or the inbox |
| Two windows on the same task's grid | | A layout change, move or zoom in one shows in the other |
| A turn finishing in a pane on screen, in the focused window | | It doesn't reach the inbox's unseen list and no desktop notification shows; a pane hidden behind a zoom doesn't count as seen |
| Narrow a pane (a layout with more columns) while following its thread | | The thread stays at its newest line |
| **Performance**: eight agents in a 3 × 2 + two layout (or 3 × 2), one running a long reply and the rest idle; then several replying at once | | Typing in a composer and scrolling stay smooth |
| Turn the Agent grid module off in Settings and restart | | No Grid tab and no Grid button on tasks |

Checked 2026-11-26 in the headless dev host on T-0013 (two dormant agents): the Grid button and view, the Layout menu and a preset, the empty region's picker, a drag swap (simulated drag events), Remove from grid and the chip menu, Alt+N, Ctrl+Shift+Z, Esc, and a real Write prompt in a pane (amber border, card, Inbox badge, Ctrl+. focusing its Allow button) denied in place. Not yet checked by hand: real mouse drags, two windows, seen and notifications from a focused window (a headless window never has focus), an outside session's pane, and the eight-pane performance check.
