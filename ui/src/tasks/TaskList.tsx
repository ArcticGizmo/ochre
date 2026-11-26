import { useCallback, useState } from 'react';
import type { SessionSummary, TaskListItem, TaskStateDto } from '../bridge';
import { ContextMenu } from '../Menu';
import { agentStatus, agentWord, byActivity, sessionName, StatePill, when, type AgentStatus } from './present';

type Filter = 'open' | TaskStateDto;

const filters: { key: Filter; label: string }[] = [
  { key: 'open', label: 'Open' },
  { key: 'active', label: 'Active' },
  { key: 'parked', label: 'Parked' },
  { key: 'backlog', label: 'Backlog' },
  { key: 'done', label: 'Done' },
];

const stateRank: Record<TaskStateDto, number> = { active: 0, parked: 1, backlog: 2, done: 3 };

export function matchesSearch(t: TaskListItem, search: string): boolean {
  const q = search.trim().toLowerCase();
  return !q || t.title.toLowerCase().includes(q) || t.id.toLowerCase().includes(q);
}

export function TasksToolbar({ search, onSearch, onNew }: { search: string; onSearch: (s: string) => void; onNew: () => void }) {
  return (
    <div className="tasks-toolbar">
      <input
        className="search"
        type="search"
        placeholder="Search tasks"
        aria-label="Search tasks"
        value={search}
        onChange={(e) => onSearch(e.target.value)}
        onKeyDown={(e) => e.key === 'Escape' && onSearch('')}
      />
      <button className="primary icon-button" aria-label="New task" title="New task" onClick={onNew}>
        <span className="plus" aria-hidden="true" />
      </button>
    </div>
  );
}

interface Props {
  tasks: TaskListItem[];
  live: SessionSummary[];
  selected: string | null;
  openSession: string | null;
  search: string;
  onSelect: (id: string) => void;
  onOpenAgent: (taskId: string, sessionId: string) => void;
  onOpenInWindow: (taskId: string) => void;
  onSearch: (s: string) => void;
  onNew: () => void;
}

const isRunning = (s: AgentStatus) => s === 'busy' || s === 'idle' || s === 'waiting';

export function TaskList({ tasks, live, selected, openSession, search, onSelect, onOpenAgent, onOpenInWindow, onSearch, onNew }: Props) {
  const [filter, setFilter] = useState<Filter>('open');
  // Tasks the user opened or closed by hand; the rest are open while one of their agents is running.
  const [toggled, setToggled] = useState<Record<string, boolean>>({});
  const [menu, setMenu] = useState<{ x: number; y: number; id: string } | null>(null);
  const closeMenu = useCallback(() => setMenu(null), []);

  const found = tasks.filter((t) => matchesSearch(t, search));
  const inFilter = (t: TaskListItem, f: Filter) => (f === 'open' ? t.state !== 'done' : t.state === f);

  // Open work first, then most recently touched: the order that answers "where do I start?".
  const shown = found.filter((t) => inFilter(t, filter)).sort((a, b) => stateRank[a.state] - stateRank[b.state] || byActivity(a, b));

  return (
    <aside className="sidebar">
      <TasksToolbar search={search} onSearch={onSearch} onNew={onNew} />
      <nav className="filters" aria-label="Filter">
        {filters.map((f) => (
          <button key={f.key} className={f.key === filter ? 'chip active' : 'chip'} aria-pressed={f.key === filter} onClick={() => setFilter(f.key)}>
            {f.label} <span className="n">{found.filter((t) => inFilter(t, f.key)).length}</span>
          </button>
        ))}
      </nav>
      <ul className="task-list">
        {shown.map((t) => {
          const statuses = t.sessions.map((s) => agentStatus(s.id, live));
          const needsYou = statuses.filter((s) => s === 'waiting').length;
          const expanded = t.sessions.length > 0 && (toggled[t.id] ?? statuses.some(isRunning));
          return (
            <li
              key={t.id}
              className={t.id === selected && !openSession ? 'selected' : ''}
              onClick={() => onSelect(t.id)}
              onContextMenu={(e) => {
                e.preventDefault();
                setMenu({ x: e.clientX, y: e.clientY, id: t.id });
              }}
            >
              <div className="row">
                <StatePill state={t.state} small />
                <span className="title">{t.title}</span>
                {t.sessions.length > 0 && (
                  <button
                    className="link expander"
                    aria-expanded={expanded}
                    aria-label={expanded ? 'Hide agents' : 'Show agents'}
                    onClick={(e) => {
                      e.stopPropagation();
                      setToggled((m) => ({ ...m, [t.id]: !expanded }));
                    }}
                  >
                    <span className={expanded ? 'chevron open' : 'chevron'} aria-hidden="true" />
                  </button>
                )}
              </div>
              <div className="meta">
                <span>{t.id}</span>
                {t.planTotal > 0 && (
                  <span>
                    {t.planDone}/{t.planTotal}
                  </span>
                )}
                <span>{when(t.updated)}</span>
                {needsYou > 0 && !expanded && <span className="needs-you">needs you</span>}
              </div>
              {t.state === 'parked' && t.parkNote ? <div className="next">{t.parkNote}</div> : t.nextStep && <div className="next">Next: {t.nextStep}</div>}
              {expanded && (
                <ul className="agent-tree">
                  {t.sessions.map((s, i) => {
                    const st = statuses[i];
                    const summary = live.find((l) => l.id === s.id);
                    return (
                      <li key={s.id}>
                        <button
                          className={`agent-row status-${st}${openSession === s.id ? ' selected' : ''}`}
                          disabled={st === 'ended'}
                          title={st === 'ended' ? 'This agent is no longer known to Ochre.' : `Open ${s.role ?? 'agent'}'s thread`}
                          onClick={(e) => {
                            e.stopPropagation();
                            onOpenAgent(t.id, s.id);
                          }}
                        >
                          <span className="dot" />
                          <span className="who">{sessionName(s)}</span>
                          <span className="act">{summary?.activity ?? agentWord(st)}</span>
                        </button>
                        {summary && summary.subagents.length > 0 && (
                          <ul className="subagents">
                            {summary.subagents.map((a, j) => (
                              <li key={j} title={a.progress ?? a.description}>
                                <span className="dot" />
                                <span className="act">{a.progress ? `${a.description} · ${a.progress}` : a.description}</span>
                              </li>
                            ))}
                          </ul>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
      {shown.length === 0 && <p className="empty">{search.trim() ? 'No tasks match.' : 'Nothing here.'}</p>}
      <ContextMenu
        at={menu}
        onClose={closeMenu}
        items={
          menu
            ? [
                { key: 'open', label: 'Open', onSelect: () => onSelect(menu.id) },
                { key: 'window', label: 'Open in new window', onSelect: () => onOpenInWindow(menu.id) },
              ]
            : []
        }
      />
    </aside>
  );
}
