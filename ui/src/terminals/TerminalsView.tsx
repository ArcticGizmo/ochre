import { lazy, Suspense, useEffect, useState } from 'react';
import { request, type TaskListItem, type TerminalDto } from '../bridge';
import { ConfirmDialog, Dialog } from '../Dialog';
import { Menu, type MenuItem } from '../Menu';
import { PathText } from '../PathText';
import { folderLeaf, terminalName } from './useTerminals';

// xterm.js is only fetched once a terminal is shown, keeping it off the window-open path.
const XtermPane = lazy(() => import('./XtermPane'));

interface Props {
  terminals: TerminalDto[];
  tasks: TaskListItem[];
  /** The tab this window shows; per window, so two windows can sit on different tabs. */
  selected: string | null;
  onSelect: (id: string | null) => void;
  onNew: () => void;
  onOpenTask: (taskId: string) => void;
  onError: (message: string) => void;
}

export function TerminalsView({ terminals, tasks, selected, onSelect, onNew, onOpenTask, onError }: Props) {
  const [hoverUrl, setHoverUrl] = useState<string | null>(null);
  const [closing, setClosing] = useState<TerminalDto | null>(null);
  const [attaching, setAttaching] = useState<TerminalDto | null>(null);
  const current = terminals.find((t) => t.id === selected) ?? terminals[0] ?? null;

  // A tab closed here or in another window: fall back to the first remaining one.
  useEffect(() => {
    if (current?.id !== selected) onSelect(current?.id ?? null);
  }, [current?.id, selected, onSelect]);

  const fail = (e: Error) => onError(e.message);
  const close = (t: TerminalDto) => {
    if (t.running) setClosing(t);
    else void request('terminals.close', { id: t.id }).catch(fail);
  };

  if (!current) {
    return (
      <div className="terminals-empty">
        <p className="empty">No terminals. Open one here, or from a task so it's listed there.</p>
        <button className="primary" onClick={onNew}>
          New terminal
        </button>
      </div>
    );
  }

  const task = current.taskId ? tasks.find((t) => t.id === current.taskId) : undefined;
  const menu: MenuItem[] = [
    { key: 'attach', label: current.taskId ? 'Attach to another task…' : 'Attach to a task…', onSelect: () => setAttaching(current) },
  ];
  if (current.taskId) {
    menu.push({ key: 'detach', label: `Detach from ${current.taskId}`, onSelect: () => void request('terminals.setTask', { id: current.id, taskId: null }).catch(fail) });
  }
  if (!current.running) {
    menu.push({ key: 'restart', label: 'Restart shell', onSelect: () => void request('terminals.restart', { id: current.id }).catch(fail) });
  }
  menu.push({ key: 'close', label: 'Close terminal…', danger: true, separatorBefore: true, onSelect: () => close(current) });

  return (
    <div className="terminals">
      <div className="term-tabs" role="tablist" aria-label="Terminals">
        {terminals.map((t) => (
          <div key={t.id} className={t.id === current.id ? 'term-tab on' : 'term-tab'}>
            <button role="tab" aria-selected={t.id === current.id} className="term-tab-main" title={t.folder} onClick={() => onSelect(t.id)}>
              <span className={t.running ? 'status status-idle' : 'status status-dormant'}>
                <span className="dot" />
              </span>
              <span className="term-tab-name">{terminalName(t)}</span>
              {t.running ? <span className="muted term-tab-sub">{folderLeaf(t.folder)}</span> : <span className="muted term-tab-sub">exited {t.exitCode}</span>}
              {t.taskId && <span className="term-chip">{t.taskId}</span>}
            </button>
            <button className="link term-close" aria-label={`Close ${terminalName(t)}`} title="Close" onClick={() => close(t)}>
              <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true">
                <path d="M3 3l6 6M9 3l-6 6" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
              </svg>
            </button>
          </div>
        ))}
        <button className="term-new" onClick={onNew} aria-label="New terminal">
          + New
        </button>
      </div>

      <div className="term-info">
        <span className="muted">{current.profileName}</span>
        <PathText path={current.folder} />
        {task ? (
          <span>
            Task{' '}
            <button className="link term-task" onClick={() => onOpenTask(task.id)}>
              {task.id}
            </button>{' '}
            <span className="muted">{task.title}</span>
          </span>
        ) : current.taskId ? (
          <span className="muted">Task {current.taskId}</span>
        ) : null}
        <span className="spacer" />
        {!current.running && (
          <button className="primary" onClick={() => void request('terminals.restart', { id: current.id }).catch(fail)}>
            Restart shell
          </button>
        )}
        <Menu ariaLabel="Terminal actions" buttonClass="link more" label={<span aria-hidden="true">⋯</span>} items={menu} />
      </div>

      <div className="term-surface">
        <Suspense fallback={<p className="empty">Loading terminal...</p>}>
          <XtermPane key={current.id} id={current.id} onLinkHover={setHoverUrl} onError={onError} />
        </Suspense>
      </div>

      <footer className="term-status">
        {hoverUrl ? (
          <>
            <code>{hoverUrl}</code>
            <span className="muted">Ctrl+click to open in your browser · middle click for a new browser window</span>
          </>
        ) : (
          <span className="muted">Ctrl+C copies a selection · Ctrl+V pastes</span>
        )}
      </footer>

      <ConfirmDialog
        open={closing !== null}
        title={`Close ${closing ? terminalName(closing) : 'terminal'}?`}
        confirmLabel="Close terminal"
        danger
        onConfirm={() => {
          if (closing) void request('terminals.close', { id: closing.id }).catch(fail);
          setClosing(null);
        }}
        onClose={() => setClosing(null)}
      >
        <p>The shell and anything still running in it, such as a dev server, will stop. Its scrollback goes with the tab.</p>
      </ConfirmDialog>

      <AttachTaskDialog terminal={attaching} tasks={tasks} onClose={() => setAttaching(null)} onError={onError} />
    </div>
  );
}

function AttachTaskDialog({ terminal, tasks, onClose, onError }: { terminal: TerminalDto | null; tasks: TaskListItem[]; onClose: () => void; onError: (m: string) => void }) {
  const [task, setTask] = useState('');
  const choosable = tasks.filter((t) => t.state !== 'done' && t.id !== terminal?.taskId);

  useEffect(() => {
    if (terminal) setTask(choosable[0]?.id ?? '');
    // Only when the dialog opens; the list reloading mustn't change a choice in progress.
  }, [terminal]);

  return (
    <Dialog
      open={terminal !== null}
      title="Attach to a task"
      onClose={onClose}
      onSubmit={() => {
        if (terminal && task) void request('terminals.setTask', { id: terminal.id, taskId: task }).catch((e: Error) => onError(e.message));
        onClose();
      }}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={!task}>
            Attach
          </button>
        </>
      }
    >
      <p className="muted">The terminal is listed on the task. It keeps running where it is.</p>
      <label className="field">
        <span>Task</span>
        <select value={task} onChange={(e) => setTask(e.target.value)}>
          {choosable.map((t) => (
            <option key={t.id} value={t.id}>
              {t.id} · {t.title}
            </option>
          ))}
        </select>
      </label>
    </Dialog>
  );
}
