import { useState } from 'react';
import type { TaskListItem, TaskStateDto } from '../bridge';

type Filter = 'open' | TaskStateDto;

const filters: { key: Filter; label: string }[] = [
  { key: 'open', label: 'Open' },
  { key: 'active', label: 'Active' },
  { key: 'parked', label: 'Parked' },
  { key: 'backlog', label: 'Backlog' },
  { key: 'done', label: 'Done' },
];

const stateRank: Record<TaskStateDto, number> = { active: 0, parked: 1, backlog: 2, done: 3 };

interface Props {
  tasks: TaskListItem[];
  selected: string | null;
  onSelect: (id: string) => void;
  onNew: () => void;
}

export function TaskList({ tasks, selected, onSelect, onNew }: Props) {
  const [filter, setFilter] = useState<Filter>('open');

  // Open work first, then most recently touched: the order that answers "where do I start?".
  const shown = tasks
    .filter((t) => (filter === 'open' ? t.state !== 'done' : t.state === filter))
    .sort((a, b) => stateRank[a.state] - stateRank[b.state] || (b.updated ?? '').localeCompare(a.updated ?? ''));

  return (
    <aside className="sidebar">
      <button className="primary new-task" onClick={onNew}>
        New task
      </button>
      <nav className="filters">
        {filters.map((f) => (
          <button key={f.key} className={f.key === filter ? 'chip active' : 'chip'} onClick={() => setFilter(f.key)}>
            {f.label}
          </button>
        ))}
      </nav>
      <ul className="task-list">
        {shown.map((t) => (
          <li key={t.id} className={t.id === selected ? 'selected' : ''} onClick={() => onSelect(t.id)}>
            <div className="row">
              <span className={`state state-${t.state}`}>{t.state}</span>
              <span className="title">{t.title}</span>
            </div>
            <div className="meta">
              <span>{t.id}</span>
              {t.planTotal > 0 && (
                <span>
                  {t.planDone}/{t.planTotal}
                </span>
              )}
            </div>
            {t.state === 'parked' && t.parkNote ? (
              <div className="next">{t.parkNote}</div>
            ) : (
              t.nextStep && <div className="next">Next: {t.nextStep}</div>
            )}
          </li>
        ))}
      </ul>
      {shown.length === 0 && <p className="empty">Nothing here.</p>}
    </aside>
  );
}
