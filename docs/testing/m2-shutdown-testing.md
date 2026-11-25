# M2 slice 5: auto-park on shutdown

What changed: a Settings dialog (gear in the title bar) with one setting, off by default: "Park tasks when Windows shuts down". When it's on and Windows asks to end the session (shutdown, restart or sign-out; WM_QUERYENDSESSION), every task with agents running is parked straight away with an automatic note: "Ochre was shut down while alpha and beta were running; last step ticked: <step>." There's no time to ask agents for notes, so they're left to end with Windows and come back dormant. The shutdown is stamped in the session ledger with what each task had before. If Ochre is still running 90 seconds later, the shutdown was cancelled, and each task that still has its automatic note is put back (state and earlier park note); a task you changed in the meantime is left alone. At the next start, a stamp left over means the shutdown went ahead: it's cleared and the tasks stay parked.

Dev builds can act out the shutdown request without shutting down: `cli dev session-ending`.

## Checks

| Try | Before | After |
|---|---|---|
| Click the gear | No settings | Settings dialog with the checkbox off and an explanation; Save writes `autoParkOnShutdown` to `settings.json` in the roaming folder |
| With it off, `cli dev session-ending` | | Nothing changes |
| Turn it on; start an agent on an active task; `cli dev session-ending` | | The task shows Parked at once, "Where you left off: Ochre was shut down while alpha was running." (plus the last ticked step if any); the agent is still running; tasks without running agents are untouched |
| Wait 90 seconds | | The task goes back to Active with its earlier park note (if it had one); the log has `ochre unparked: the shutdown was cancelled` |
| Simulate again, then Resume the task yourself before 90 seconds | | It stays as you left it |
| Real shutdown with it on and an agent running; start Windows and Ochre | | Today shows the task Parked with the automatic note; its agent is dormant and resumes with its conversation when you send to it |
| Real shutdown with it off | | Tasks keep their state; agents come back dormant |

## Automated

`OchreSettingsTests`: off by default, persists, keeps unknown keys, sets aside a file it can't parse. `SessionLifecycleTests`: off changes nothing; on parks only tasks with running agents with the note and a ledger stamp, without stopping agents; a cancel puts back untouched tasks and leaves alone ones the user changed; a stamp found at start is cleared and the tasks stay parked.
