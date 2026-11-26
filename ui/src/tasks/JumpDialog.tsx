import { useEffect, useMemo, useRef, useState } from 'react';
import type { TaskListItem, TaskStateDto } from '../bridge';
import { Dialog } from '../Dialog';
import { matchesSearch } from './TaskList';
import { byActivity, StatePill } from './present';

interface Props {
  open: boolean;
  tasks: TaskListItem[];
  onClose: () => void;
  /** Enter: show the task in this window. */
  onOpen: (id: string) => void;
  /** Ctrl+Enter: show it in a window of its own. */
  onOpenInWindow: (id: string) => void;
}

const stateRank: Record<TaskStateDto, number> = { active: 0, parked: 1, backlog: 2, done: 3 };

/** "Jump to task": type part of a title or id, pick with the arrows, Enter to go. Ctrl+K in a window, or the global hotkey. */
export function JumpDialog({ open, tasks, onClose, onOpen, onOpenInWindow }: Props) {
  const [search, setSearch] = useState('');
  const [at, setAt] = useState(0);
  const list = useRef<HTMLUListElement>(null);

  useEffect(() => {
    if (open) {
      setSearch('');
      setAt(0);
    }
  }, [open]);

  // Open work first, then most recently touched; done tasks only when the search names them.
  const shown = useMemo(
    () =>
      tasks
        .filter((t) => matchesSearch(t, search) && (t.state !== 'done' || search.trim() !== ''))
        .sort((a, b) => stateRank[a.state] - stateRank[b.state] || byActivity(a, b))
        .slice(0, 50),
    [tasks, search],
  );
  const current = shown[Math.min(at, shown.length - 1)];

  useEffect(() => {
    list.current?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [at, shown]);

  const go = (id: string, ownWindow: boolean) => {
    onClose();
    if (ownWindow) onOpenInWindow(id);
    else onOpen(id);
  };

  return (
    <Dialog
      open={open}
      title="Jump to task"
      onClose={onClose}
      onSubmit={() => current && go(current.id, false)}
      actions={<span className="muted jump-hint">Enter opens it here · Ctrl+Enter in its own window · Esc closes</span>}
    >
      <input
        className="jump-search"
        autoFocus
        placeholder="Search tasks by title or id"
        aria-label="Search tasks"
        aria-controls="jump-results"
        value={search}
        onChange={(e) => {
          setSearch(e.target.value);
          setAt(0);
        }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            const step = e.key === 'ArrowDown' ? 1 : -1;
            setAt((i) => Math.max(0, Math.min(shown.length - 1, i + step)));
          } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            if (current) go(current.id, true);
          }
        }}
      />
      {shown.length === 0 ? (
        <p className="empty">{search.trim() ? 'No tasks match.' : 'No open tasks.'}</p>
      ) : (
        <ul className="jump-results" id="jump-results" role="listbox" ref={list}>
          {shown.map((t) => (
            <li
              key={t.id}
              role="option"
              aria-selected={t === current}
              className={t === current ? 'selected' : ''}
              onMouseMove={() => setAt(shown.indexOf(t))}
              onClick={(e) => go(t.id, e.ctrlKey || e.metaKey)}
            >
              <StatePill state={t.state} small />
              <span className="jump-title">{t.title}</span>
              <span className="muted jump-id">{t.id}</span>
            </li>
          ))}
        </ul>
      )}
    </Dialog>
  );
}
