import { useCallback, useEffect, useMemo, useState, type ComponentType } from 'react';
import { on, request, type AppInfo, type TaskListItem } from './bridge';
import { runningModules, type AppContext } from './modules';
import { SessionView } from './sessions/SessionView';
import { SettingsDialog } from './SettingsDialog';
import { useSessions } from './sessions/useSessions';
import { NewTaskDialog } from './tasks/NewTaskDialog';
import { TaskBoard } from './tasks/TaskBoard';
import { TaskList } from './tasks/TaskList';
import { TaskView } from './tasks/TaskView';
import { Today } from './tasks/Today';
import { useOpenInWindow } from './windows/useOpenInWindow';

// The kernel's views; modules add theirs after these (ADR 0012).
const kernelViews: { key: string; label: string }[] = [
  { key: 'today', label: 'Today' },
  { key: 'tasks', label: 'Tasks' },
  { key: 'board', label: 'Board' },
];

interface Thread {
  sessionId: string;
  parkNote: string | null;
}

export function App() {
  const [info, setInfo] = useState<AppInfo | null>(null);
  const [tasks, setTasks] = useState<TaskListItem[]>([]);
  // All of this is per window, so two windows can each sit on their own view and task.
  const [view, setView] = useState('today');
  // Each module view's parameter (e.g. which terminal tab), kept per window.
  const [viewParams, setViewParams] = useState<Record<string, string | null>>({});
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  // An agent's thread shown in place of the selected task (Tasks), or in place of Today (Today).
  const [taskThread, setTaskThread] = useState<string | null>(null);
  const [thread, setThread] = useState<Thread | null>(null);
  const [creating, setCreating] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Until the host has said which task this window opened on, don't report one back.
  const [windowKnown, setWindowKnown] = useState(false);
  const live = useSessions();
  const modules = useMemo(() => runningModules(info?.modules ?? []), [info]);
  const { openInWindow, dialog: openInWindowDialog } = useOpenInWindow(tasks, setError);

  const refreshList = useCallback(() => {
    request('tasks.list', {})
      .then(setTasks)
      .catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    request('app.info', {})
      .then(setInfo)
      .catch((e: Error) => setError(e.message));
    request('window.get', {})
      .then((w) => {
        if (w.task) {
          setView('tasks');
          setSelected(w.task);
        }
      })
      .catch(() => {})
      .finally(() => setWindowKnown(true));
    refreshList();
    return on('tasks.changed', refreshList);
  }, [refreshList]);

  // The task this window shows, if any: what the host saves for the next start and matches when a task is opened
  // in its own window. Only the Tasks view counts; a task left selected behind Today isn't on screen.
  const shownTask = view === 'tasks' ? selected : null;
  useEffect(() => {
    if (windowKnown) request('window.setTask', { task: shownTask }).catch(() => {});
  }, [windowKnown, shownTask]);

  const shownTitle = tasks.find((t) => t.id === shownTask)?.title;
  useEffect(() => {
    // The host puts this in the window's title bar, so windows can be told apart in Alt+Tab and Task View.
    document.title = shownTask ? `${shownTask} ${shownTitle ?? ''}`.trim() : 'Ochre';
  }, [shownTask, shownTitle]);

  const openTask = useCallback((id: string) => {
    setView('tasks');
    setSelected(id);
    setTaskThread(null);
  }, []);

  const show = (v: string) => {
    setView(v);
    // Today's tab always lands on Today itself, not on a thread left open there.
    if (v === 'today') setThread(null);
  };

  const setParam = useCallback((key: string, param: string | null) => setViewParams((p) => ({ ...p, [key]: param })), []);

  const ctx: AppContext = useMemo(
    () => ({
      tasks,
      openTask,
      openInWindow,
      showView: (key, param) => {
        setView(key);
        if (param !== undefined) setParam(key, param);
      },
      onError: setError,
    }),
    [tasks, openTask, openInWindow, setParam],
  );

  const moduleView = modules.find((m) => m.view?.key === view)?.view;
  const taskSections = modules.flatMap((m) => (m.TaskSection ? [{ id: m.id, Section: m.TaskSection }] : []));
  const folderStatuses = modules.flatMap((m) => (m.FolderStatus ? [{ id: m.id, Status: m.FolderStatus }] : []));

  let body;
  if (moduleView) {
    body = moduleView.render({ param: viewParams[moduleView.key] ?? null, setParam: (p) => setParam(moduleView.key, p), ctx });
  } else if (view === 'today') {
    body = (
      <main className="content">
        {thread ? (
          <SessionView key={thread.sessionId} id={thread.sessionId} backLabel="Today" parkNote={thread.parkNote} onBack={() => setThread(null)} onError={setError} />
        ) : (
          <Today
            tasks={tasks}
            live={live}
            folderStatuses={folderStatuses}
            onOpenTask={openTask}
            onOpenAgent={(sessionId, task) => setThread({ sessionId, parkNote: task.parkNote })}
            onOpenSession={(sessionId) => setThread({ sessionId, parkNote: null })}
            onNew={() => setCreating(true)}
            onError={setError}
          />
        )}
      </main>
    );
  } else if (view === 'board') {
    body = (
      <main className="content">
        <TaskBoard tasks={tasks} live={live} search={search} onOpen={openTask} onSearch={setSearch} onNew={() => setCreating(true)} />
      </main>
    );
  } else {
    body = (
      <div className="split">
        <TaskList
          tasks={tasks}
          live={live}
          selected={selected}
          openSession={taskThread}
          search={search}
          onSelect={(id) => {
            setSelected(id);
            setTaskThread(null);
          }}
          onOpenAgent={(taskId, sessionId) => {
            setSelected(taskId);
            setTaskThread(sessionId);
          }}
          onOpenInWindow={openInWindow}
          onSearch={setSearch}
          onNew={() => setCreating(true)}
        />
        <main className="content">
          {selected ? (
            <TaskView
              key={selected}
              id={selected}
              openSession={taskThread}
              onOpenSession={setTaskThread}
              sections={taskSections}
              folderStatuses={folderStatuses}
              ctx={ctx}
              onError={setError}
              onDeleted={() => setSelected(null)}
            />
          ) : (
            <p className="empty">{tasks.length ? 'Select a task.' : 'Create a task to start. Agents are launched from tasks.'}</p>
          )}
        </main>
      </div>
    );
  }

  const views: { key: string; label: string; Badge?: ComponentType }[] = [...kernelViews, ...modules.flatMap((m) => (m.view ? [m.view] : []))];

  return (
    <div className="app">
      <header className="titlebar">
        <span className="brand">Ochre</span>
        {info?.profile && <span className="profile">{info.profile}</span>}
        <nav className="main-nav" aria-label="Main">
          {views.map(({ key, label, Badge }) => (
            <button key={key} className={view === key ? 'nav on' : 'nav'} aria-current={view === key ? 'page' : undefined} onClick={() => show(key)}>
              {label}
              {Badge && <Badge />}
            </button>
          ))}
        </nav>
        <span className="spacer" />
        {info && <span className="version">v{info.version}</span>}
        <button className="link gear" aria-label="Settings" title="Settings" onClick={() => setSettingsOpen(true)}>
          <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
            <circle cx="8" cy="8" r="2.2" fill="none" stroke="currentColor" strokeWidth="1.4" />
            <path
              d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4"
              stroke="currentColor"
              strokeWidth="1.4"
              strokeLinecap="round"
            />
          </svg>
        </button>
      </header>
      {error && (
        <div className="banner" role="alert">
          {error}
          <button className="link" onClick={() => setError(null)}>
            Dismiss
          </button>
        </div>
      )}
      <div className="body">{body}</div>
      <SettingsDialog open={settingsOpen} onClose={() => setSettingsOpen(false)} />
      {openInWindowDialog}
      <NewTaskDialog
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(task) => {
          setCreating(false);
          openTask(task.id);
          refreshList();
        }}
      />
    </div>
  );
}
