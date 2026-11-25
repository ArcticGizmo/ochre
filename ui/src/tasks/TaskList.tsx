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
  onCreate: (title: string) => void;
}

export function TaskList({ tasks, selected, onSelect, onCreate }: Props) {
  const [filter, setFilter] = useState<Filter>('open');
  const [title, setTitle] = useState('');

  // Open work first, then most recently touched: the order that answers "where do I start?".
  const shown = tasks
    .filter((t) => (filter === 'open' ? t.state !== 'done' : t.state === filter))
    .sort((a, b) => stateRank[a.state] - stateRank[b.state] || (b.updated ?? '').localeCompare(a.updated ?? ''));

  function submit() {
    const t = title.trim();
    if (!t) return;
    onCreate(t);
    setTitle('');
  }

  return (
    <aside className="sidebar">
      <form
        className="new-task"
        onSubmit={(e) => {
          e.preventDefault();
          submit();
        }}
      >
        <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="New task title" maxLength={200} />
        <button type="submit" disabled={!title.trim()}>
          Add
        </button>
      </form>
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
