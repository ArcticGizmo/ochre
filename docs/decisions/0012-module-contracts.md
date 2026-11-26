# 0012: Module contracts, first cut

- Status: accepted
- Date: 2026-11-26

## Context

ADR 0007 makes every feature beyond the kernel a module on the same contracts plugins will use, and defers designing them until real features exist (M4). M1 to M3 built tasks, sessions, park, Today, observed sessions, auto-park on shutdown and terminal tabs. Of those, terminal tabs and auto-park on shutdown are features a user can do without; the rest is the kernel. The git, sprig and Jira modules arrive in M4, so the contracts must also leave room for worktree providers and link resolvers.

What the terminal feature actually needed from the host, read from its code: bridge handlers and their contract entries, a way to broadcast events, the task store (to check a task id), platform services (ConPTY, DPAPI), its own data folder, and hooks for quit and for Windows ending the session. Auto-park needed the task store, the session manager, the session ledger and the session-ending hook.

Options for enabling and disabling:

| Option | Verdict |
|---|---|
| Live, without a restart | Turning terminals off would have to close running shells and every window's UI would have to drop a view mid-use; a lot of code for something done rarely |
| At start, with a restart offered | Chosen. A module is either running for the whole session or not loaded at all |

## Decision

- **Module.** `IModule` (Core): a stable lowercase `Id`, a `Name` and `Description` for Settings, `DefaultEnabled`, the bridge `Requests` and `Events` it owns, and `Start(IModuleContext)` returning an `IDisposable` that stops it. Constructing a module does nothing; all work happens in `Start`, so tools can list modules and their contracts without starting them.
- **Context.** `IModuleContext` hands a module what the kernel offers: the log, its own data folder, the task store, the session manager and ledger, platform services by interface (`Service<T>()`), `AddBridge` (handlers registered on every window's dispatcher), `Broadcast`, `OnSessionEnding`, and the extension points as they arrive (`AddWorktreeProvider`, later `AddLinkResolver`, `AddTaskSource`). A module reaches nothing else of the host.
- **Bridge.** A module's message and event names start with its id (`terminals.open`, `terminals.output`). It owns its message records and its JSON source-generation context. The full contract is the kernel's list plus every first-party module's, whether on or off, so the generated UI types never depend on settings; the host only registers the handlers of modules that started.
- **Lifetime.** `ModuleHost` (Core) starts enabled modules in a fixed order after the kernel and stops them in reverse on quit. A module whose `Start` throws is logged, marked failed, and its contributions are dropped; the host carries on.
- **Settings.** `settings.json` keeps `modules: { "<id>": true|false }`; a missing entry means the module's default. Changes apply at the next start, and Settings offers to restart Ochre.
- **UI.** `app.info` lists the modules that started. The web UI has a registry (`ui/src/modules/`) where each module declares its contributions (a title-bar view with an optional badge, a task-detail section), and the app renders only those of started modules. A contribution navigates through a small app context (open a task, show a view with a parameter) rather than reaching into another module.
- **Where code lives.** Each first-party module is a project under `modules/`, referencing Core. OS-specific pieces stay in `Ochre.Platform.<os>` behind Core interfaces and reach the module through `Service<T>()`.

## Consequences

- Terminal tabs and auto-park on shutdown move into `modules/`; "Park tasks when Windows shuts down" stops being a setting and becomes a module that is off by default.
- Every contract is shaped as data (names, records, events), which is what an out-of-process plugin host (M7) can proxy. In-process first-party modules still run as trusted code, so `IModule` and `IModuleContext` are internal to Ochre's own assemblies (shared by `InternalsVisibleTo`); they hand modules the kernel's internal services directly.
- Turning a module on or off costs a restart. If that proves annoying for a particular module, it can add live start and stop later without changing the contract.
- The UI bundle still contains every first-party module's code; switching one off hides it rather than leaving it out of the download. Fine for an app served from disk.
