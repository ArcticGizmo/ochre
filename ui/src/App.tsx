import { useEffect, useState } from 'react';
import { request, type AppInfo } from './bridge';

export function App() {
  const [info, setInfo] = useState<AppInfo | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    request('app.info', {})
      .then(setInfo)
      .catch((e: Error) => setError(e.message));
  }, []);

  return (
    <div className="app">
      <header className="titlebar">
        <span className="brand">Ochre</span>
        {info?.profile && <span className="profile">{info.profile}</span>}
        <span className="spacer" />
        {info && <span className="version">v{info.version}</span>}
      </header>
      <aside className="sidebar">
        <h2>Tasks</h2>
        <p className="empty">No tasks yet.</p>
      </aside>
      <main className="content">
        {error ? <p className="error">{error}</p> : <p className="empty">Select a task, or create one.</p>}
      </main>
    </div>
  );
}
