import { useEffect, useState } from 'react';
import { on, request, type GitFolderStatus } from '../../bridge';

interface Entry {
  statuses: GitFolderStatus[] | null;
  listeners: Set<() => void>;
  loading: boolean;
  again: boolean;
}

// One fetch per task however many folders on the page show it; shared by every component in this window.
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
  request('git.status', { taskId })
    .then((s) => {
      e.statuses = s;
    })
    // A deleted task or a host restart: keep what was shown; the next refresh tries again.
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

// Status follows what the user and agents do, never a timer: coming back to the window, an agent's turn ending,
// or the task's folders changing.
function wire() {
  if (wired) return;
  wired = true;
  const all = () => entries.forEach((_, id) => refresh(id));
  window.addEventListener('focus', all);
  on('window.shown', all);
  on('git.changed', (e) => refresh(e.taskId));
  on('tasks.changed', (e) => refresh(e.id));
}

/** The git state of each of the task's folders; null until the first answer. */
export function useGitStatus(taskId: string): GitFolderStatus[] | null {
  const [, setTick] = useState(0);
  useEffect(() => {
    wire();
    let e = entries.get(taskId);
    if (!e) {
      e = { statuses: null, listeners: new Set(), loading: false, again: false };
      entries.set(taskId, e);
    }
    const listener = () => setTick((t) => t + 1);
    e.listeners.add(listener);
    // The first component to show a task fetches it; later ones use what's there.
    if (e.listeners.size === 1) refresh(taskId);
    return () => {
      e.listeners.delete(listener);
    };
  }, [taskId]);
  return entries.get(taskId)?.statuses ?? null;
}
