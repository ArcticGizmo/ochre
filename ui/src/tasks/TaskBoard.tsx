import { useState } from 'react';
import type { SessionSummary, TaskListItem } from '../bridge';
import { agentStatus, agentWord, byActivity, stateLabel, states } from './present';
import { matchesSearch, TasksToolbar } from './TaskList';

/** Done grows without bound, so the board shows the latest few and folds the rest. */
const doneShown = 5;

interface Props {
  tasks: TaskListItem[];
  live: SessionSummary[];
  search: string;
  onOpen: (id: string) => void;
  onSearch: (s: string) => void;
  onNew: () => void;
}

export function TaskBoard({ tasks, live, search, onOpen, onSearch, onNew }: Props) {
  const [allDone, setAllDone] = useState(false);
  const found = tasks.filter((t) => matchesSearch(t, search)).sort(byActivity);

  return (
    <div className="page board-page">
      <TasksToolbar search={search} onSearch={onSearch} onNew={onNew} />
      <div className="board">
        {states.map((s) => {
          const column = found.filter((t) => t.state === s);
          const visible = s === 'done' && !allDone ? column.slice(0, doneShown) : column;
          return (
            <section key={s} className="board-col" aria-label={stateLabel[s]}>
              <h2 className={`board-col-head state-${s}`}>
                <span className="dot" />
                <span className="name">{stateLabel[s]}</span>
                <span className="n">{column.length}</span>
              </h2>
              {visible.map((t) => (
                <button key={t.id} className="board-card" onClick={() => onOpen(t.id)}>
                  <span className="t">{t.title}</span>
                  <code className="i">{t.id}</code>
                  {t.state === 'parked' && t.parkNote ? <span className="x">{t.parkNote}</span> : t.nextStep && t.state !== 'done' && <span className="x">Next: {t.nextStep}</span>}
                  <span className="f">
                    <span>{t.planTotal > 0 ? `${t.planDone}/${t.planTotal}` : 'No plan yet'}</span>
                    <span className="grow" />
                    {t.sessions.map((r) => {
                      const st = agentStatus(r.id, live);
                      return (
                        <span key={r.id} className={`ag status-${st}`} title={`${r.role ?? 'agent'}: ${agentWord(st)}`}>
                          <span className="dot" />
                          {r.role ?? r.id.slice(0, 4)}
                        </span>
                      );
                    })}
                  </span>
                </button>
              ))}
              {s === 'done' && column.length > doneShown && (
                <button className="link" onClick={() => setAllDone((v) => !v)}>
                  {allDone ? 'Show fewer' : `Show ${column.length - doneShown} more`}
                </button>
              )}
              {column.length === 0 && <p className="empty small">None.</p>}
            </section>
          );
        })}
      </div>
    </div>
  );
}
