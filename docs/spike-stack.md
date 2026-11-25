# M0: stack spike

## Question

Can a .NET 10 host with one web UI inside a system WebView give Monaco- and xterm.js-grade panes with acceptable memory, startup and latency, while keeping Perch's C# logic reusable? ([ADR 0002](decisions/0002-stack-dotnet-host-web-ui.md))

## Build (throwaway, in `spikes/stack/`)

1. A .NET 10 tray host (single instance) that opens a window containing a WebView with a Vite + React + TypeScript app served via a virtual host mapping.
2. A task list rendered from three hard-coded markdown task files.
3. One **xterm.js** tab connected to a ConPTY owned by the host, running PowerShell.
4. One **Monaco** editor opening a file from disk through the bridge, plus a diff view.
5. A bridge with typed messages validated on the host.

Build it with each shell candidate that is cheap to try:

| Shell | Notes |
|---|---|
| Avalonia 12 + `Avalonia.Controls.WebView` + `TrayIcon` | Known stack; cross-platform; check the WebView package licence |
| Photino.NET | Thin, cross-platform WebView host; tray via a separate library |
| WinForms/WPF + WebView2 | Windows-only baseline for comparison |

Optionally compare against an Electron build of the same UI to see what bundled Chromium costs.

## Measure

| Metric | How | Target |
|---|---|---|
| Tray idle memory, no window | Working set after 5 min | < 120 MB |
| One window open, idle | Working set across host + WebView processes | < 350 MB |
| Cold start to tray | Stopwatch from launch | < 1.5 s |
| Window open from tray | Stopwatch | < 500 ms (warm) |
| Typing latency in xterm.js | Key to glyph, rough (high-speed capture or `performance.now` round trip) | Feels instant; < 30 ms |
| Terminal throughput | Time to stream `Get-ChildItem -Recurse` of a large tree / a 50 MB file | No UI freeze; comparable to Windows Terminal within 2× |
| Bridge round trip | 10k small messages | < 1 ms median |
| Package size | Published output | Record |

Also note: how hard the bridge was to type safely, the hot-reload story, and any EDR prompts.

## Decide

- **Accept B** if the targets are roughly met and the bridge copes with terminal output.
- **Fall back to A** (Avalonia native + web islands) if the WebView costs too much at idle or the bridge can't carry terminal throughput.
- **Consider Electron** only if WebView quirks (WebKit on macOS) become a blocker and the memory cost is acceptable.

Record the numbers and the choice in ADR 0002, then delete or archive the spike.
