# 0014: Ochre ships with Velopack from GitHub releases, as Perch does

- Status: accepted
- Date: 2026-11-26

## Context

Until now Ochre ran only from a checkout (`run.cmd`, a Debug build on the dev profile), which is rebuilt and restarted with every change to Ochre itself. Using it day to day needs a copy that is installed, stays put while Ochre is developed, and updates when the user chooses.

Perch already solves this on the same machine (and under the same endpoint protection): a self-contained publish, packed with Velopack into a per-user installer, released from GitHub Actions on a version tag, installed by a one-line `install.ps1` that checks a published SHA-256, and updated from inside the app. Ochre's WebView2 UI changes nothing here: the WebView2 runtime ships with Windows 11, the UI is a folder beside the exe, and Ochre already keeps WebView2's profile under `%LOCALAPPDATA%`, outside the folder Velopack replaces on update.

| Option | Verdict |
|---|---|
| Velopack, as Perch | Chosen: proven here, per user with no admin, delta updates, an install and uninstall hook for PATH and registry entries |
| MSIX | Needs signing and a package identity; restricts the tray host, child processes and registry writes Ochre relies on |
| A zip and a script | No uninstaller, no Start menu entry, and updates would be hand-rolled |

## Decision

- **`publish.ps1`** builds a release: the bridge types checked, the UI built, the host published self-contained as one `ochre.exe` (no single-file compression, as Perch measured), the CLI published NativeAOT as `ochre.com` beside it (falling back to a self-contained single file where the C++ build tools are missing), then `vpk pack` (pinned in `.config/dotnet-tools.json`, in step with the `Velopack` package) and `SHA256SUMS.txt` into `releases\`.
- **The package id is `OchreApp`, not `Ochre`.** Velopack installs to `%LocalAppData%\<id>` and deletes that folder on uninstall, and `%LocalAppData%\Ochre` is where Ochre keeps logs, the window layout, the session ledger and terminal scrollback. The user sees "Ochre" (the Start menu entry and the uninstaller); only the installer's file name and install folder carry `OchreApp`.
- **An installed copy is the normal profile.** A Release build uses `%APPDATA%\Ochre` and `%LOCALAPPDATA%\Ochre`, its own pipe and its own notification identity, so it runs beside a dev build without sharing anything.
- **Install hooks**: after install and update, the install folder goes on the per-user PATH (so `ochre` works in any terminal; the PATH is edited as its raw registry string, copied from Perch with its tests); before uninstall, that entry, the sign-in start and the notification identity are removed.
- **Updates come from the repository's GitHub releases** (`ArcticGizmo/ochre`, public, so no token). The host checks a minute after it starts and every six hours, and when the user asks (Settings). A newer version shows in the title bar; applying it asks first, downloads, then quits Ochre the normal way (agents stop and come back dormant, terminals save their scrollback) while Velopack's updater swaps the files and starts the new version. Nothing updates without the user asking. `OCHRE_UPDATE_FEED` points an installed copy at a local `releases\` folder to try an update before publishing it. A tag with a suffix (`v0.2.0-beta.1`) is a prerelease, which the update check skips.
- **Starting at sign-in** is a setting offered only to an installed copy (a per-user run key, `ochre.exe --tray`).
- **GitHub Actions**: `release.yml` runs on a `v*` tag: tests, `publish.ps1` on `windows-latest`, then a separate job with the only write permission makes the release and its `SHA256SUMS.txt` from exactly the files published. `ci.yml` builds, checks the bridge types and runs the tests on pull requests. Actions are pinned to commit SHAs; pnpm comes from Node's corepack, pinned by `packageManager` in `ui/package.json` and the lockfile.
- **`install.ps1`** (adapted from Perch) installs the latest release, or a pinned one, after checking the installer against `SHA256SUMS.txt`. Nothing is code-signed yet.

## Consequences

- Day-to-day Ochre is the installed copy; development carries on in the checkout with `run.cmd` on the dev profile.
- A release is `git tag vX.Y.Z` and `git push origin vX.Y.Z`; `<Version>` in `src/Ochre.Host/Ochre.Host.csproj` is only the default for a local `publish.ps1`.
- The installer is about 90 MB, most of it the self-contained runtime and the Windows SDK projection; trimming is a later size question.
- Unsigned files meet SmartScreen when downloaded through a browser; `install.ps1` avoids that, and signing can come later without changing the process.
- macOS will need its own packaging (Velopack supports it) when the macOS head exists.
