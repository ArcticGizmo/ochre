# 0011: The host owns terminals, filters their output and keeps their scrollback

- Status: accepted
- Date: 2026-11-26

## Context

M3 adds terminal tabs. Three requirements pull on where terminal state lives:

- Windows are views (ADR 0008), and there can be several at once (roadmap, "Multiple windows"). A dev server keeps running with every window closed, and a window opened later must show what it printed meanwhile.
- Tabs come back after a quit with their scrollback (roadmap M3 exit), and scrollback often holds secrets (security threat 6).
- Terminal output is untrusted: escape sequences can write the clipboard, disguise links or spoof titles (security threat 7). The filter is a security boundary, so it should be testable in the existing xUnit suite.

Options for scrollback:

| Option | Verdict |
|---|---|
| Each window's xterm.js serialises its buffer (`addon-serialize`) and sends it to the host | Needs a window open to have anything to save; two windows disagree; output printed while no window is open is lost |
| A headless terminal emulator on the host | Exact screen state, but there's no maintained .NET one; VS Code does this with `@xterm/headless` in Node, which Ochre doesn't run |
| The host keeps the filtered output stream in a ring buffer and replays it | Chosen. Works with zero windows, every window gets the same bytes, and it's a few dozen lines |

Options for the escape-sequence filter:

| Option | Verdict |
|---|---|
| Only in xterm.js (no clipboard addon, an OSC 52 handler that swallows, a `linkHandler`) | Uses xterm's own parser, so no divergence, but it's in the untrusted renderer, needs a JS test runner, and the saved scrollback still carries the sequences |
| On the host, before output reaches the ring buffer or any window | Chosen, with the xterm-side settings kept as a second layer |

## Decision

- The host owns each PTY (ConPTY behind a Core interface). Output is filtered, coalesced on a 16 ms timer, appended to a per-terminal ring of about one million characters (trimmed by whole chunks), and broadcast to every window with a sequence number. A window joining late asks for the ring and its sequence number, then applies only later chunks.
- The filter passes everything except OSC strings through unchanged. OSC strings are parsed with xterm's terminators (BEL, ST, a bare ESC, CAN/SUB abort), checked against an allow list, and re-emitted with the filter's own terminator, so xterm sees exactly the sequence the filter checked. OSC 52 and unknown numbers are dropped; OSC 8 keeps only `http` and `https` targets. An incomplete sequence at the end of a read is held back, so chunks (and so the ring's trim points) fall on sequence boundaries.
- Terminal state lives in `%LOCALAPPDATA%\Ochre\terminals\`, not in task files: a PTY belongs to one machine, and task files roam. A terminal may name the task it belongs to.
- Saved scrollback is encrypted with DPAPI for the current user, with a per-tab switch to keep none.
- The renderer chooses a shell only by profile id from a list the host detected; it never sends a command line to start.

## Consequences

- Replaying a stream isn't the same as restoring a screen: if the ring was trimmed past the point where a full-screen program switched to the alternate screen, a late window can draw it imperfectly until the program repaints. Restore after a quit resets attributes and leaves the alternate screen before drawing a divider, so a restored tab is always readable.
- Every window receives every terminal's output, viewing it or not. Fine for a handful of tabs; per-window subscriptions are the fix if many busy tabs make it cost.
- Several windows can show one terminal. The PTY takes the size of whichever window last resized it, and only a focused window sends input, so query replies aren't sent twice.
- Terminal input over the bridge means script running in the page can run commands. That makes security threat 5's mitigations (strict CSP, sanitised markdown, no remote content) the control for terminals too; the bridge limits what starts a terminal, not what is typed into one.
