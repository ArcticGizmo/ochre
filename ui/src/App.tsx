import { useCallback, useEffect, useState } from 'react';
import { on, request, type AppInfo, type TaskListItem } from './bridge';
import { SessionView } from './sessions/SessionView';
import { useSessions } from './sessions/useSessions';
import { NewTaskDialog } from './tasks/NewTaskDialog';
import { TaskBoard } from './tasks/TaskBoard';
import { TaskList } from './tasks/TaskList';
import { TaskView } from './tasks/TaskView';
import { Today } from './tasks/Today';

type View = 'today' | 'tasks' | 'board';

const views: { key: View; label: string }[] = [
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
  const [view, setView] = useState<View>('today');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  // An agent's thread shown in place of the selected task (Tasks), or in place of Today (Today).
  const [taskThread, setTaskThread] = useState<string | null>(null);
  const [thread, setThread] = useState<Thread | null>(null);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const live = useSessions();

  const refreshList = useCallback(() => {
    request('tasks.list', {})
      .then(setTasks)
      .catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    request('app.info', {})
      .then(setInfo)
      .catch((e: Error) => setError(e.message));
    refreshList();
    return on('tasks.changed', refreshList);
  }, [refreshList]);

  const openTask = (id: string) => {
    setView('tasks');
    setSelected(id);
    setTaskThread(null);
  };

  const show = (v: View) => {
    setView(v);
    // Today's tab always lands on Today itself, not on a thread left open there.
    if (v === 'today') setThread(null);
  };

  let body;
  if (view === 'today') {
    body = (
      <main className="content">
        {thread ? (
          <SessionView key={thread.sessionId} id={thread.sessionId} backLabel="Today" parkNote={thread.parkNote} onBack={() => setThread(null)} onError={setError} />
        ) : (
          <Today
            tasks={tasks}
            live={live}
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
          onSearch={setSearch}
          onNew={() => setCreating(true)}
        />
        <main className="content">
          {selected ? (
            <TaskView key={selected} id={selected} openSession={taskThread} onOpenSession={setTaskThread} onError={setError} onDeleted={() => setSelected(null)} />
          ) : (
            <p className="empty">{tasks.length ? 'Select a task.' : 'Create a task to start. Agents are launched from tasks.'}</p>
          )}
        </main>
      </div>
    );
  }

  return (
    <div className="app">
      <header className="titlebar">
        <span className="brand">Ochre</span>
        {info?.profile && <span className="profile">{info.profile}</span>}
        <nav className="main-nav" aria-label="Main">
          {views.map((v) => (
            <button key={v.key} className={view === v.key ? 'nav on' : 'nav'} aria-current={view === v.key ? 'page' : undefined} onClick={() => show(v.key)}>
              {v.label}
            </button>
          ))}
        </nav>
        <span className="spacer" />
        {info && <span className="version">v{info.version}</span>}
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
