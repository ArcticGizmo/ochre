# run.cmd, cli.cmd and the console CLI

What changed: two scripts at the repo root for trying a checkout, and `src/Ochre.Cli` (`ochre-cli.exe`), a console app that talks to the running host over its per-user pipe. Both scripts use the dev profile (tasks in `%APPDATA%\Ochre-dev`).

Run them from cmd in the repo root. From PowerShell use `.\run.cmd` and `.\cli.cmd`.

| Try | Before | After |
|---|---|---|
| `run` | Build steps by hand | "Building the CLI / Generating bridge types / Building the UI / Building the host / Started."; the tray icon and window appear |
| `run` again while it's running | Build failed: exe locked | The running host quits first, then it rebuilds and starts again |
| `run -NoBuild` | | Starts the last build without building |
| `run -Bad` | | Usage line, exit code 2 |
| `cli status` | | `running 0.1.0 profile=dev pid=...`, or `not running (profile dev)` with exit code 3 |
| `cli close`, then `cli open` | | The window closes (tray stays), then reopens |
| `cli quit` | | Prints `quit` once the host has exited; the tray icon goes |
| `cli open` with nothing running | | `starting`; the host starts from this checkout's build |
| `cli dev eval "document.title"` | | `"Ochre"` |
| `cli dev capture captures\test.png` | | Prints the full path; the PNG shows the window |
| `cli bogus` | | Usage text on stderr, exit code 2 |
| Break the build (e.g. a typo in `src/Ochre.Cli/Program.cs`), then `cli status` | | The build log, then `cli: build failed, not running ochre-cli` |
