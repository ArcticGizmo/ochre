# M4 slice 1: modules

What changed: features beyond the kernel are now modules you can turn on and off in Settings (ADR 0012). There are two so far: **Terminals** (on by default) and **Park tasks when Windows shuts down** (off by default; it replaces the old setting, and if you had that on, the module starts on). Choices apply when Ochre restarts, and Settings offers "Restart Ochre now" after you save one. Tasks, agents, parking, Today and outside sessions are the kernel and always work.

## Checks

| Try | Before | After |
|---|---|---|
| Open Settings (gear) | One checkbox for parking on shutdown | A Modules list: Terminals (ticked) and Park tasks when Windows shuts down, each with a description |
| Untick Terminals, Save | | It reads "off after a restart"; the button becomes **Restart Ochre now**, with a note on what a restart does |
| **Restart Ochre now** | | Ochre closes and reopens by itself; the log says `stopped; restarting`, then starts with no `module ... started` line |
| Look at the window | | No Terminals tab in the title bar; a task has no Terminals section. Create a task, add a step, start an agent: all as before |
| Turn Terminals back on, restart | | The Terminals tab is back, and any tabs you had before turning it off come back with their scrollback |
| Tick Park tasks when Windows shuts down, restart, run `cli dev session-ending` with an agent running | | The task parks with the automatic note (as in `m2-shutdown-testing.md`); with the module off, the command says nothing was parked |
| If you had "Park tasks when Windows shuts down" on before this change | | The module shows ticked; `settings.json` swaps `autoParkOnShutdown` for `modules` the first time you save |

Automated: `ModuleHostTests` (only enabled modules start, a failing module is left off without its handlers, reverse stop order, events only under the module's own name), `The_kernel_works_with_every_module_off` in `SessionLifecycleTests`, and `OchreSettingsTests` for the choices and the old setting.
