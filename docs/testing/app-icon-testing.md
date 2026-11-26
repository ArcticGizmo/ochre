# App icon

`ochre.svg` at the repo root is the one source of the logo. `tools\gen-icons.cmd` renders it through SkiaSharp and Svg.Skia (`tools/Ochre.IconGen`) into `src/Ochre.Platform.Windows/Assets/ochre.ico` (13 frames, 16 to 256 px, each drawn from the vector at its own size), `ochre-256.png` beside it (the notification identity's logo) and `docs/assets/ochre-icon.png` (512 px, the README header). Small frames are 32-bit DIBs with straight alpha; only the 256 px frame is PNG.

The tray takes the frame for exactly `SM_CXSMICON` at the system DPI (16 at 100 %, 20 at 125 %, 24 at 150 %, 28 at 175 %, 32 at 200 %), so Windows never shrinks a larger frame for it. Windows get every frame and pick their own per monitor. The `.exe` carries the same icon.

| Try | Before | After |
|---|---|---|
| Look at the tray at 100 % scale | A plain ochre disc | The Ochre ring, crisp, with no grey or dark fringe on a light or dark taskbar |
| Change the display scale to 125 %, 150 % and 175 %, then restart Ochre | | Still crisp at each (a scale change while running keeps the old size until the next start) |
| A window's title bar and the taskbar button; Alt+Tab | Disc | The ring at each size |
| `ochre.exe` in Explorer (large icons) | Generic app icon | The ring |
| A notification's app name in the notification centre (normal run, not headless) | Disc | The ring |
| The README on GitHub | No logo | The ring centred above the title |
| Edit `ochre.svg`, run `tools\gen-icons.cmd --preview captures\<timestamp>-icon-frames.png` | | All three assets rewritten; the preview shows each frame up to 48 px at 8×, on light and dark |

Checked 2026-11-26: the generator's preview sheet, every tray size loaded back through `System.Drawing.Icon` (the same call the tray makes) and drawn at 8×, and the dev host starting with the embedded icons. The tray, title bars and notification centre on a real screen are for the user to check.
