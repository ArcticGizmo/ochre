import { request, type SessionSummary, type TaskListItem } from '../bridge';
import { OutsideSessions } from '../sessions/OutsideSessions';
import { agentStatus, agentWord, byActivity, ParkNote, sessionName, StatePill, when } from './present';

interface Props {
  tasks: TaskListItem[];
  live: SessionSummary[];
  onOpenTask: (id: string) => void;
  onOpenAgent: (sessionId: string, task: TaskListItem) => void;
  onOpenSession: (sessionId: string) => void;
  onNew: () => void;
  onError: (message: string) => void;
}

/** The morning view: every Active and Parked task, with where you left off and one click back into each agent. */
export function Today({ tasks, live, onOpenTask, onOpenAgent, onOpenSession, onNew, onError }: Props) {
  const shown = tasks.filter((t) => t.state === 'active' || t.state === 'parked').sort(byActivity);
  const active = shown.filter((t) => t.state === 'active').length;

  return (
    <div className="page today">
      <header className="page-header">
        <div className="grow">
          <h1>Today</h1>
          <div className="muted">{new Date().toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' })}</div>
        </div>
        <span className="pill state-active">
          <span className="dot" />
          Active {active}
        </span>
        <span className="pill state-parked">
          <span className="dot" />
          Parked {shown.length - active}
        </span>
        <button onClick={onNew}>New task</button>
      </header>

      {shown.length === 0 && <p className="empty">Nothing is active or parked. Start a task from Tasks, or create one.</p>}

      {shown.map((t) => (
        <article key={t.id} className="today-card">
          <div className="row">
            <div className="grow">
              <button className="title-link" onClick={() => onOpenTask(t.id)}>
                {t.title}
              </button>
              <code className="muted id">{t.id}</code>
            </div>
            <StatePill state={t.state} />
            <span className="muted when">{when(t.updated)}</span>
          </div>

          {t.parkNote && (
            <div className="today-note">
              <h3>
                Where you left off {t.parkedAt && <span className="h3-aside">· parked {when(t.parkedAt)}</span>}
              </h3>
              <ParkNote note={t.parkNote} />
            </div>
          )}

          {t.planTotal > 0 && (
            <div className="row">
              <span className="label">Next</span>
              <div className="vals">
                {t.nextStep ? (
                  <>
                    <span className="box" aria-hidden="true" />
                    <span className="grow">{t.nextStep}</span>
                  </>
                ) : (
                  <span className="grow muted">Every step is ticked.</span>
                )}
                <span className="muted small">
                  {t.planDone} / {t.planTotal}
                </span>
                <span className="progress" aria-hidden="true">
                  <span style={{ width: `${Math.round((100 * t.planDone) / t.planTotal)}%` }} />
                </span>
              </div>
            </div>
          )}

          {(t.sessions.length > 0 || t.state === 'parked') && (
            <div className="row">
              <span className="label">Agents</span>
              <div className="vals">
                {t.sessions.length === 0 && <span className="muted">None.</span>}
                {t.sessions.map((s) => {
                  const status = agentStatus(s.id, live);
                  const name = sessionName(s);
                  return status === 'ended' ? (
                    <span key={s.id} className="agent-chip status-ended" title="This agent is no longer known to Ochre, so it can't be resumed.">
                      <span className="dot" />
                      {name}
                      <span className="st">{agentWord(status)}</span>
                    </span>
                  ) : (
                    <button key={s.id} className={`agent-chip status-${status}`} title={status === 'dormant' ? `Open ${name}'s thread, ready to resume` : `Open ${name}'s thread`} onClick={() => onOpenAgent(s.id, t)}>
                      <span className="dot" />
                      {name}
                      <span className="st">{agentWord(status)}</span>
                    </button>
                  );
                })}
              </div>
              {t.state === 'parked' && (
                <button onClick={() => request('tasks.setState', { id: t.id, state: 'active' }).catch((e: Error) => onError(e.message))}>Resume task</button>
              )}
            </div>
          )}

          {t.links.length > 0 && (
            <div className="row">
              <span className="label">Links</span>
              <div className="vals">
                {t.links.map((l) => (
                  <span key={l.id} className="link-ref">
                    <span className="kind">{l.kind}</span>
                    {l.ref}
                    {l.title && <span className="muted"> {l.title}</span>}
                  </span>
                ))}
              </div>
            </div>
          )}
        </article>
      ))}

      <OutsideSessions live={live} tasks={tasks} onOpen={onOpenSession} onError={onError} />
    </div>
  );
}
