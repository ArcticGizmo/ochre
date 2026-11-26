# Design: terminal tabs (M3 slice 2)

Agreed 2026-11-26 as a first pass, before the UI was changed (`docs/ux-conventions.md`, Process): terminals get their own title-bar tab, and a task lists its terminals. Published as a private design canvas at https://claude.ai/artifact/SUbwuVPcoscgvpYsWhNHGh; this folder is its local copy. Ochre's own colours, as in `docs/design/m2-today/`, rather than a design system.

Artboards in `project/`: `Main.dc.html` (the Terminals view with a dev server running, a link hovered and the tab menu open), `Task.dc.html` (a task's Terminals section), `NewTerminal.dc.html` (the dialog, opened from a task) and `Restored.dc.html` (a tab after Ochre quit and relaunched; slice 3). They render only inside the canvas, which supplies `support.js`. Sample tasks, paths and output are made up.
