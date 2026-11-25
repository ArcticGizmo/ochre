# 0008: A background tray host owns all state; windows are views

- Status: accepted
- Date: 2026-11-25

## Context

Startup should be paid once, and monitoring should continue with no window open (like Claude desktop or Perch). Terminals should keep running when windows close.

## Decision

A single-instance host process with a tray icon owns the task store, controlled sessions, terminal PTYs, module services and watchers. Windows are views onto it and can be opened on any monitor. The host's services sit behind an in-process service boundary so they could move to a separate process later.

## Consequences

- Closing all windows keeps agents and terminals alive; only quitting stops them.
- The host's idle footprint matters (target in [spike-stack.md](../spike-stack.md)).
- A host crash takes everything down until a process split is justified.
