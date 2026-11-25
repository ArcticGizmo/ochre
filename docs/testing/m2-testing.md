# M2 exit run: park at night, resume next morning

Exit criterion (roadmap): park two tasks at night; next morning "Today" shows both with notes; one click resumes a session with its context.

Walked on 2026-11-25 in the dev window, with two real agents. Per-slice checks are in `m2-dormant-testing.md`, `m2-park-testing.md`, `m2-today-testing.md`, `m2-observed-testing.md` and `m2-shutdown-testing.md`.

## The run

| Step | What to do | What should happen | Result |
|---|---|---|---|
| 1 | Create two tasks with the repo set; make both Active | Both on Today | T-0009 and T-0010 |
| 2 | Start an agent on each with a first message that gives it something to remember ("Remember the code word MAPLE-7 … reply: ready") | Each replies; Today shows each agent "ready" | alpha and beta replied "Ready" |
| 3 | Park each task with a note of your own | Each goes Parked at once; each agent writes its own line with `task park-note` and goes dormant | Both notes in under 5 s: your note, then `- alpha: … MAPLE-7 …` and `- beta: … OAK-3 …` |
| 4 | Quit Ochre and start it again (the night) | Agents come back dormant | Both dormant |
| 5 | Look at Today | Both tasks, Parked, "Where you left off" with your note and the agent's line, each agent's chip dormant | As expected |
| 6 | Click alpha's chip | Its thread with yesterday's conversation, alpha's park note above the composer, composer focused | As expected |
| 7 | Send "What code word did I ask you to remember?" | Resume and send; it answers from its earlier context | "MAPLE-7" |

## Also checked in the same run

- Auto-park on shutdown (slice 5): with the setting on and both agents running, `cli dev session-ending` parked both tasks with "Ochre was shut down while alpha was running.", left the task without agents alone, and put both back after 90 s with the ledger stamp cleared. The setting was turned off again afterwards.
- Sessions started outside Ochre (slice 4): agents Ochre launched never appeared under "Other Claude sessions"; a terminal session in the repo did, with suggestions.

## Found

- A repo or agent folder given with doubled separators (`C:\\a\\b`) was stored as typed. Paths are now normalised on the way in.
- Sending to an agent while its task is Parked resumes the agent but leaves the task Parked; you have to click Resume task as well. Left as is: whether the first message should make the task Active is a choice for the user.
