import { useCallback, useEffect, useState } from 'react';
import { on, request, type AppInfo, type TaskListItem } from './bridge';
import { TaskList } from './tasks/TaskList';
import { TaskView } from './tasks/TaskView';

export function App() {
  const [info, setInfo] = useState<AppInfo | null>(null);
  const [tasks, setTasks] = useState<TaskListItem[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

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

  async function create(title: string) {
    try {
      const task = await request('tasks.create', { title });
      setSelected(task.id);
      refreshList();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="app">
      <header className="titlebar">
        <span className="brand">Ochre</span>
        {info?.profile && <span className="profile">{info.profile}</span>}
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
      <TaskList tasks={tasks} selected={selected} onSelect={setSelected} onCreate={create} />
      <main className="content">
        {selected ? (
          <TaskView key={selected} id={selected} onError={setError} />
        ) : (
          <p className="empty">{tasks.length ? 'Select a task.' : 'Create a task to start. Agents are launched from tasks.'}</p>
        )}
      </main>
    </div>
  );
}
