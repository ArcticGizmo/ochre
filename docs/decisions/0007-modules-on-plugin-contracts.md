# 0007: First-party features are modules on the plugin contracts

- Status: accepted
- Date: 2026-11-25

## Context

The core principle is composition: turn off what you don't use and everything else still works. Perch grew features first and settings second, which leads to a large flat settings registry and scattered feature checks.

## Decision

The kernel (tasks, sessions, workspace, event bus, module host, windows, agent surface) works with zero modules. Every other feature, including first-party ones (git graph, Jira, sprig, terminal, Roost grid, editor), is a module that contributes through the same extension points third-party plugins use (`TaskSource`, `LinkResolver`, `WorktreeProvider`, `SessionLauncher`, `Panel`, `TaskAction`, `Signal`, `EventHandler`, `Widget`). A feature toggle is a module being on or off.

First-party modules load in process (trusted code). Third-party plugins run out of process with consent and sandboxing.

## Consequences

- The plugin API is exercised by real features, so it stays honest.
- Extension points must be designed early; expect to revise them as the first modules land (M4).
- A test should start the app with every module off and exercise the kernel.
