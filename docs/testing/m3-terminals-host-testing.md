# M3 slice 1: terminals in the host

What changed: the host can run shells in pseudoconsoles (ConPTY) and serve them over the bridge, ahead of any terminal UI (slice 2). Output is filtered for dangerous escape sequences (ADR 0011, security threat 7), coalesced every 16 ms, kept as scrollback in the host and broadcast to every window with a sequence number. The renderer picks a shell by profile id only.

There's no UI yet, so these checks drive the bridge from the dev window with `cli dev eval`. Call `src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe` directly from PowerShell rather than `cli.cmd`, so cmd doesn't treat `>` in the script as a redirect. Each eval below is one line; `t.send(id, type, payload)` posts a request and `t.msgs[id]` holds its reply.

Set up a listener once:

```js
(function(){ var t = window.__t = { msgs: {}, out: {}, seqs: {} }; window.chrome.webview.addEventListener('message', function(e){ var m = JSON.parse(e.data); if (m.event === 'terminals.output') { var p = m.payload; t.out[p.id] = (t.out[p.id] || '') + p.data; (t.seqs[p.id] = t.seqs[p.id] || []).push(p.seq); } else if (typeof m.id === 'number' && m.id in t.msgs) { t.msgs[m.id] = m; } }); t.send = function(id, type, payload){ t.msgs[id] = null; window.chrome.webview.postMessage(JSON.stringify({ id: id, type: type, payload: payload })); }; return 'ok'; })()
```

## Checks

| Try | Before | After |
|---|---|---|
| `t.send(1, 'terminals.profiles', {})`, then read `t.msgs[1]` | Unknown message type | The shells on this machine: PowerShell (if pwsh is on PATH), Windows PowerShell, Command Prompt, Git Bash (if installed) |
| Open pwsh in a repo with startup command `Write-Output ochre-terminal-ok` (`terminals.open`, cols 100, rows 30) | | A terminal dto with `running: true`; `t.out[id]` shows the prompt, the command and `ochre-terminal-ok` |
| Open with folder `relative`, profile `nope`, or a startup command containing `\r\n` | | Refused: "choose an absolute folder", "unknown shell", "a startup command is one line..." |
| Type into it: `Write-Host ('A' + [char]27 + ']52;c;ZXZpbA==' + [char]7 + 'B')` then Enter (`terminals.input`) | The clipboard sequence would reach xterm | The output reads `AB`: the OSC 52 is gone |
| Print an OSC 8 link to `javascript:alert(1)` and one to `https://example.com` | | The javascript one comes through as `ESC]8;;BEL` (text kept, no target); the https one keeps its target |
| `terminals.attach` for the terminal | | `data` is the scrollback so far; `seq` equals the last `terminals.output` seq seen |
| `terminals.resize` to 120x40, then to 5000 cols | | The first is accepted (pwsh redraws); the second is refused, "terminal size out of range" |
| Type `exit 3` | | The output ends with a dim `[shell exited with code 3]`; `terminals.list` shows `running: false, exitCode: 3`; further input is refused |
| `terminals.restart` | | A fresh prompt in the same tab, after the old scrollback |
| `terminals.close` | | Ack; `terminals.list` is empty; the host log has `terminal t-... closed` |
