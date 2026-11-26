import { useEffect, useSyncExternalStore } from 'react';
import { on, request, type AttentionSnapshot } from '../bridge';

// One copy per window, shared by the badge, the inbox and Ctrl+. so they never disagree.
const empty: AttentionSnapshot = { items: [], needsYou: 0 };
let current = empty;
let started = false;
const subscribers = new Set<() => void>();

function set(next: AttentionSnapshot) {
  current = next;
  subscribers.forEach((s) => s());
}

function start() {
  if (started) return;
  started = true;
  on('attention.changed', set);
  request('attention.list', {})
    .then(set)
    .catch(() => {});
}

function subscribe(cb: () => void) {
  start();
  subscribers.add(cb);
  return () => subscribers.delete(cb);
}

/** What needs the user, as the host last said (ADR 0013). */
export function useAttention(): AttentionSnapshot {
  return useSyncExternalStore(subscribe, () => current);
}

export function attentionNow(): AttentionSnapshot {
  start();
  return current;
}

/**
 * Tells the host the user has this session's thread in front of them, which marks its finished turn read: now, when
 * the window takes focus, and when a turn finishes while it's on screen. Only while this window has focus, so a
 * thread left open in a background window doesn't swallow the item. Null while the thread is mounted but hidden.
 */
export function useReportSeen(sessionId: string | null) {
  useEffect(() => {
    if (!sessionId) return;
    const report = () => {
      if (!document.hasFocus()) return;
      request('attention.seen', { sessionId }).catch(() => {});
    };
    report();
    window.addEventListener('focus', report);
    const off = on('attention.changed', (s) => {
      if (s.items.some((i) => i.sessionId === sessionId && !i.needsYou)) report();
    });
    return () => {
      window.removeEventListener('focus', report);
      off();
    };
  }, [sessionId]);
}
