import { useEffect, useState } from 'react';
import { on, request, type SprigTaskStatus } from '../../bridge';

interface Entry {
  status: SprigTaskStatus | null;
  listeners: Set<() => void>;
  loading: boolean;
  again: boolean;
}

// One fetch per task however many places on the page show it; shared by every component in this window.
const entries = new Map<string, Entry>();
let wired = false;

function refresh(taskId: string) {
  const e = entries.get(taskId);
  if (!e || e.listeners.size === 0) return;
  if (e.loading) {
    e.again = true;
    return;
  }
  e.loading = true;
  request('sprig.status', { taskId })
    .then((s) => {
      e.status = s;
    })
    // A deleted task or a host restart: keep what was shown; the next change tries again.
    .catch(() => {})
    .finally(() => {
      e.loading = false;
      e.listeners.forEach((l) => l());
      if (e.again) {
        e.again = false;
        refresh(taskId);
      }
    });
}

// Sprig pushes every change through the host's one `sprig watch`, so there's nothing to poll.
function wire() {
  if (wired) return;
  wired = true;
  on('sprig.changed', () => entries.forEach((_, id) => refresh(id)));
  on('tasks.changed', (e) => refresh(e.id));
}

/** The sprig workspaces the task's folders are in; null until the first answer. */
export function useSprigStatus(taskId: string): SprigTaskStatus | null {
  const [, setTick] = useState(0);
  useEffect(() => {
    wire();
    let e = entries.get(taskId);
    if (!e) {
      e = { status: null, listeners: new Set(), loading: false, again: false };
      entries.set(taskId, e);
    }
    const listener = () => setTick((t) => t + 1);
    e.listeners.add(listener);
    if (e.listeners.size === 1) refresh(taskId);
    return () => {
      e.listeners.delete(listener);
    };
  }, [taskId]);
  return entries.get(taskId)?.status ?? null;
}
