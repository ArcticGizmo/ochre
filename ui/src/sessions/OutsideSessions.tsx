import { request, type SessionSummary, type TaskListItem } from '../bridge';
import { Menu } from '../Menu';
import { PathText } from '../PathText';
import { agentWord, sessionName, statusOf } from '../tasks/present';

const link = (sessionId: string, taskId: string, onError: (m: string) => void) =>
  request('sessions.bind', { id: sessionId, taskId }).catch((e: Error) => onError(e.message));

interface SectionProps {
  live: SessionSummary[];
  tasks: TaskListItem[];
  onOpen: (sessionId: string) => void;
  onError: (message: string) => void;
}

/**
 * Claude sessions running outside Ochre that no task has yet, each with the tasks it looks like it belongs to.
 * Ochre only suggests; linking is the user's click (explicit over magic).
 */
export function OutsideSessions({ live, tasks, onOpen, onError }: SectionProps) {
  const outside = live.filter((s) => s.observed && !s.taskId);
  if (outside.length === 0) return null;
  const open = tasks.filter((t) => t.state !== 'done');
  const title = (id: string) => tasks.find((t) => t.id === id)?.title ?? id;

  return (
    <section className="outside">
      <h2>
        Other Claude sessions <span className="h3-aside">{outside.length}</span>
      </h2>
      <p className="muted small">Started outside Ochre. Link one to a task to see it there; Ochre only reads it.</p>
      {outside.map((s) => {
        const status = statusOf(s);
        return (
          <article key={s.id} className="outside-row">
            <div className="row">
              <button className={`agent-chip status-${status}`} title="Open its thread (read-only)" onClick={() => onOpen(s.id)}>
                <span className="dot" />
                {sessionName(s)}
                <span className="st">{agentWord(status)}</span>
              </button>
              <PathText path={s.cwd} />
              {s.branch && <code className="muted">{s.branch}</code>}
              <span className="grow" />
              <Menu
                ariaLabel="Link to a task"
                buttonClass="picker"
                label={
                  <>
                    Link to…
                    <span className="chevron" aria-hidden="true" />
                  </>
                }
                items={open.map((t) => ({ key: t.id, label: <span className="grow">{`${t.id} ${t.title}`}</span>, onSelect: () => link(s.id, t.id, onError) }))}
              />
            </div>
            {s.suggestions.length > 0 && (
              <div className="suggestions">
                {s.suggestions.map((g) => (
                  <button key={g.taskId} className="suggestion" onClick={() => link(s.id, g.taskId, onError)}>
                    Link to <strong>{g.taskId}</strong> {title(g.taskId)}
                    <span className="muted"> · {g.reason}</span>
                  </button>
                ))}
              </div>
            )}
          </article>
        );
      })}
    </section>
  );
}

interface SuggestionProps {
  taskId: string;
  live: SessionSummary[];
  onOpen: (sessionId: string) => void;
  onError: (message: string) => void;
}

/** On a task: outside sessions that look like they belong to it, one click to link each. */
export function LinkSuggestions({ taskId, live, onOpen, onError }: SuggestionProps) {
  const candidates = live.filter((s) => s.observed && !s.taskId && s.suggestions.some((g) => g.taskId === taskId));
  if (candidates.length === 0) return null;
  return (
    <>
      {candidates.map((s) => (
        <div key={s.id} className="review-bar suggest-bar">
          <span>
            A Claude session in <PathText path={s.cwd} />
            {s.branch && (
              <>
                {' '}
                on <code>{s.branch}</code>
              </>
            )}{' '}
            looks related ({s.suggestions.find((g) => g.taskId === taskId)?.reason}).
          </span>
          <button className="link" onClick={() => onOpen(s.id)}>
            Open
          </button>
          <button onClick={() => link(s.id, taskId, onError)}>Link to this task</button>
        </div>
      ))}
    </>
  );
}
