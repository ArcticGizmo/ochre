import { useEffect, useState } from 'react';
import { on, request, type SessionSummary } from '../bridge';

/** The host's live sessions, kept current from sessions.update events. */
export function useSessions(): SessionSummary[] {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);

  useEffect(() => {
    let alive = true;
    const load = () =>
      request('sessions.list', {})
        .then((list) => alive && setSessions(list))
        .catch(() => {});
    load();
    const off = on('sessions.update', (e) => {
      setSessions((list) => {
        const i = list.findIndex((s) => s.id === e.id);
        if (i < 0) {
          // A session another window started; fetch the list for its details.
          load();
          return list;
        }
        if (list[i].status === e.header.status) return list;
        const next = [...list];
        next[i] = { ...list[i], status: e.header.status };
        return next;
      });
    });
    return () => {
      alive = false;
      off();
    };
  }, []);

  return sessions;
}
