# M5 slice 2: attention model

The host now keeps a list of everything that needs the user (ADR 0013). Nothing in the UI shows it yet (that's slice 3, the inbox view), so these checks go through the bridge in a headless dev host. The automated tests (`AttentionBoardTests`, with the in-memory `claude`) cover what raises and clears each item; these checks confirm the real host and a real agent agree.

## Setup

1. `$env:OCHRE_DEV_HEADLESS='1'; $env:OCHRE_DEV_DESKTOP='<a spare virtual desktop guid>'; .\run.cmd`
2. In one `dev eval`, install a listener that keeps replies and events:

```
ochre-cli.exe dev eval "(function(){ window.__r = {}; window.__ev = []; window.chrome.webview.addEventListener('message', function(e){ var d = JSON.parse(e.data); if (d.id) window.__r[d.id] = d; if (d.event === 'attention.changed') window.__ev.push(d.payload); }); return 'ok'; })()"
```

3. Send a request with `window.chrome.webview.postMessage(JSON.stringify({id: 900001, type: 'attention.list', payload: {}}))`, then read `JSON.stringify(window.__r[900001])` in a second eval. `postMessage` takes a string, not an object.

## Checks

| Try | Before | After |
|---|---|---|
| `attention.list` with no agents waiting | No such message | `{items: [], needsYou: 0}` |
| `attention.dismiss` with `{id: 'nope'}` | No such message | Refused: "that item can't be dismissed, or is gone" |
| `attention.seen` with `{sessionId: 'nope'}` | No such message | Refused: "no such session" |
| Start an agent on a task in default permission mode with "Run `git status` with Bash" | Nothing | An `attention.changed` event with one `permission` item: title "wants to use Bash", detail naming the command, `requestId` set, `needsYou: 1` |
| Answer it with `sessions.respond` | | The item goes; when the turn ends, a `finished` item with the reply's last line, `needsYou: 0` |
| `attention.seen` with that agent's session id | | The `finished` item goes |
| `attention.dismiss` on the permission item's id while it's pending | | Refused; the item stays |
| Start `claude` in a terminal in one of the task's repos, link it to the task, and give it something that asks permission | | A `waiting` item under the task; `attention.dismiss` hides it until it waits again |
| Kill a running agent's `claude` process | | A `failed` item saying it exited with a code |
| Quit and restart the dev host | Items listed | Nothing from before the restart is listed (sessions come back dormant; ADR 0013) |

Checked 2026-11-26 in the headless dev host: the first three rows. The rows with a real agent wait for slice 3, where the inbox makes them visible without the bridge.
