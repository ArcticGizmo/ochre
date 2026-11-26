# Notifications spike: results

M5 slice 1, run 2026-11-26 with the user at the screen. Code: `spikes/notifications/` (a console app, `notify-spike`), never referenced by `src/`. Plan: `docs/implementation-plan.md`, M5 slice 1.

## What was built

The WinRT path only: `Windows.UI.Notifications.ToastNotificationManager` from a project targeting `net10.0-windows10.0.19041.0`, no NuGet packages. Windows App SDK's `AppNotificationManager` wasn't tried, since the WinRT path did everything asked of it without a runtime to install.

- **Identity**: an AppUserModelID under `HKCU\Software\Classes\AppUserModelId\Ochre.Spike.Notifications` with `DisplayName`, `IconUri` and `IconBackgroundColor`. No Start menu shortcut, no `SetCurrentProcessExplicitAppUserModelID`. Notifications showed as "Ochre (spike)".
- **Content**: a `header` per task, `useButtonStyle` with Success and Critical buttons, a choice box (`input type="selection"`), text boxes with a button beside them (`hint-inputId`), an `appLogoOverride` per status (PNGs drawn at start into `%LOCALAPPDATA%\Ochre-spike`), an attribution line, a data-bound progress bar, and the `reminder` and `urgent` scenarios.

## Findings

| Question | Answer |
|---|---|
| Works unpackaged, no installer | Yes, from the registry key alone |
| Header per task | Yes: the notification centre groups them under "T-0042 Fix the login redirect" |
| Buttons reach the running process | Yes, in-process through `ToastNotification.Activated`, with the arguments: Allow, Deny |
| Typed and chosen input | Yes, in `ToastActivatedEventArgs.UserInput`: the choice box (`answer=cookie`) and a text box (`feedback=...`) |
| `reminder` scenario | Stays on screen until acted on |
| Replace in place, remove, clear group | Yes: same tag and group replaces; `History.Remove(tag, group, aumid)` and `RemoveGroup` take it away |
| Progress | `NotificationData` with `Update(data, tag, group)` moves the bar without a new pop-up |
| A shown notification that isn't acted on | Raises `Dismissed` with `TimedOut` when it moves to the centre; the process should ignore it |
| Output size | `Microsoft.Windows.SDK.NET.dll` adds 23.7 MB and `WinRT.Runtime.dll` 0.5 MB to the output |

## Not yet checked

- **A click after the process has quit** (protocol activation, `ochre-spike:`). The key was registered but no click was tried. This decides slice 4's design: if a protocol launch works and the endpoint protection on this machine accepts it, every button can be a protocol link that the new `ochre` process forwards to the running host over the existing instance pipe, so clicks work whether Ochre is running or not and no COM activator (`LocalServer32`) is ever registered. If not, buttons stay in-process (foreground activation) and a click after quit does nothing.
- **Clicking from the notification centre** rather than the pop-up, while running.
- **`urgent` against `reminder`**, and whether either gets through Do Not Disturb.
- **The reply box on a finished turn** (`f`, Send). Same mechanism as the plan's feedback box, which worked, so low risk.

## Recommendation

- Build on the WinRT path in `Ochre.Platform.Windows`, which moves to `net10.0-windows10.0.19041.0`; Core stays `net10.0`. Accept the 24 MB for now; trimming the projection is a release-size question for later.
- Separate ids per profile: `Ochre` for the installed app and `Ochre.Dev` for the dev profile, each registered on start (cheap, idempotent) and removed by the uninstaller.
- Use `reminder` for permissions, questions and plans (they block an agent); default for finished, failed and waiting.
- Before slice 4: run the protocol check (steps below) and record the result here.

## Rerunning

```
dotnet run --project spikes/notifications
```

Commands are listed on start. For the after-quit check: `mode` (switches buttons to `ochre-spike:` links and registers the scheme), `p`, `quit`, then click Allow in the notification centre; a new spike console should open and `%LOCALAPPDATA%\Ochre-spike\activations.log` gets a `protocol launch` line. `dotnet run --project spikes/notifications -- unregister` removes both registry keys and clears the spike's notifications.
