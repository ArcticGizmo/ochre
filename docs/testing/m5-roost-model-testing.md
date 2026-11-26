# M5 slice 5: Roost model

The `roost` module (on by default) keeps a grid per task in `%LOCALAPPDATA%\Ochre\roost\roost.json` (`Ochre-dev` for the dev profile). There's no grid view yet (slice 6), so these checks go through the bridge in a headless dev host, the same way as `docs/testing/m5-attention-testing.md` (install a reply listener with one `dev eval`, `postMessage` a JSON string, read the reply in a second eval).

| Try | Before | After |
|---|---|---|
| `roost.get {taskId, aspect: 1.6}` for a task with two agents | No such message | Two columns, one agent in each, numbered 1 and 2; `preset: "Columns2"`; `unplaced` empty; nine presets listed |
| Same for a task with three agents, `aspect: 2.5` | | Three columns (one + two on a narrower stage) |
| `roost.applyPreset {preset: "Grid2x2"}` | | Four regions; the agents keep their reading order; the new regions are empty; other windows get `roost.changed` |
| `roost.applyPreset` back to `"Full"` | | One region with the first agent; the rest are listed in `unplaced` |
| `roost.assign` an unplaced agent to region 1 | | It takes region 1; the agent that was there is now unplaced |
| `roost.assign` an agent already in region 2 to region 1 | | The two swap |
| `roost.unassign` an agent, then start a new agent on the task | | The removed one stays out; the new one fills the first empty region, if any |
| `roost.zoom {regionId}` twice | | `zoomed` is that region, then null |
| `roost.assign` with a session from another task, `roost.applyPreset {preset: "7"}`, `roost.get` for a task that doesn't exist | | Refused: "that agent isn't on this task", "unknown layout", "no such task" |
| Quit and restart the dev host, `roost.get` again | | The same grid |
| Delete the task, restart | | Its grid is gone from `roost.json` |
| Turn the Agent grid module off in Settings and restart | | `roost.*` messages are refused; nothing else changes |

Checked 2026-11-26 in the headless dev host on T-0013: the first row, a preset applied, and the three refusals. The rest wait for the grid view, where they're visible.
