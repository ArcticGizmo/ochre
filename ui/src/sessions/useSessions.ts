import { useEffect, useState } from 'react';
import { on, request, type SessionSummary } from '../bridge';

/** The host's sessions with their status and current activity, kept current from sessions.update events. */
export function useSessions(): SessionSummary[] {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);

  useEffect(() => {
    let alive = true;
    const load = () =>
      request('sessions.list', {})
        .then((list) => alive && setSessions(list))
        .catch(() => {});
    load();
    // Sessions come and go outside Ochre, and an outside session's suggestions follow the tasks, so both reload.
    const offChanged = on('sessions.changed', load);
    const offTasks = on('tasks.changed', load);
    const off = on('sessions.update', (e) => {
      setSessions((list) => {
        const i = list.findIndex((s) => s.id === e.id);
        if (i < 0) {
          // A session another window started; fetch the list for its details.
          load();
          return list;
        }
        const h = e.header;
        const s = list[i];
        if (s.status === h.status && s.activity === h.activity && JSON.stringify(s.subagents) === JSON.stringify(h.subagents)) return list;
        const next = [...list];
        next[i] = { ...s, status: h.status, activity: h.activity, subagents: h.subagents };
        return next;
      });
    });
    return () => {
      alive = false;
      off();
      offChanged();
      offTasks();
    };
  }, []);

  return sessions;
}
