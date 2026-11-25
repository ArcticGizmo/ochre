import type { SessionStatusDto, SessionSummary, TaskStateDto } from '../bridge';

export const states: TaskStateDto[] = ['backlog', 'active', 'parked', 'done'];

export const stateLabel: Record<TaskStateDto, string> = { backlog: 'Backlog', active: 'Active', parked: 'Parked', done: 'Done' };

export function StatePill({ state, small }: { state: TaskStateDto; small?: boolean }) {
  return (
    <span className={`pill state-${state}${small ? ' small' : ''}`}>
      <span className="dot" />
      {stateLabel[state]}
    </span>
  );
}

/**
 * A host status; "closed" for a session started outside Ochre whose process has gone (its transcript can still be
 * read); "ended" for an agent on the task that the host no longer has (it can't be opened).
 */
export type AgentStatus = SessionStatusDto | 'closed' | 'ended';

const agentWords: Record<AgentStatus, string> = { busy: 'working', idle: 'ready', waiting: 'needs you', dormant: 'dormant', closed: 'closed', ended: 'ended' };

export const agentWord = (s: AgentStatus) => agentWords[s];

export function statusOf(s: SessionSummary | { status: SessionStatusDto; observed: boolean }): AgentStatus {
  return s.observed && s.status === 'dormant' ? 'closed' : s.status;
}

export function agentStatus(sessionId: string, live: SessionSummary[]): AgentStatus {
  const s = live.find((l) => l.id === sessionId);
  return s ? statusOf(s) : 'ended';
}

/** A session's name in a one-line space: its role, else the folder it runs in, else its short id. */
export function sessionName(s: { id: string; role: string | null; cwd: string }): string {
  return s.role ?? (s.cwd.split(/[\\/]/).filter(Boolean).pop() || s.id.slice(0, 8));
}

export interface ParkLine {
  role: string | null;
  text: string;
}

/** Agents' notes are stored as "- role: text" lines under your own note, so they're split back out to show who said what. */
export function parkNoteLines(note: string): ParkLine[] {
  return note
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean)
    .map((l) => {
      const m = /^- ([^:]{1,60}): (.+)$/.exec(l);
      return m ? { role: m[1], text: m[2] } : { role: null, text: l };
    });
}

export function ParkNote({ note }: { note: string }) {
  return (
    <>
      {parkNoteLines(note).map((l, i) =>
        l.role ? (
          <p key={i} className="muted">
            <strong className="who">{l.role}:</strong> {l.text}
          </p>
        ) : (
          <p key={i}>{l.text}</p>
        ),
      )}
    </>
  );
}

const time = (d: Date) => d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });

/** "12 min ago", "14:05", "yesterday 18:42", "Tue 24 Nov": the coarser the further back. */
export function when(iso: string | null, now = new Date()): string {
  if (!iso) return '';
  const d = new Date(iso);
  const mins = Math.floor((now.getTime() - d.getTime()) / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins} min ago`;
  const day = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const days = Math.round((day(now) - day(d)) / 86400000);
  if (days === 0) return time(d);
  if (days === 1) return `yesterday ${time(d)}`;
  return d.toLocaleDateString(undefined, { weekday: days < 7 ? 'short' : undefined, day: 'numeric', month: 'short' });
}

/** Most recent first; tasks never touched sort last. */
export const byActivity = (a: { updated: string | null }, b: { updated: string | null }) => (b.updated ?? '').localeCompare(a.updated ?? '');
