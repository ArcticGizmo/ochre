import { useEffect, useState } from 'react';
import { request, type TaskSummary } from './bridge';
import { TerminalPanel } from './panels/TerminalPanel';
import { EditorPanel } from './panels/EditorPanel';
import { BenchPanel } from './panels/BenchPanel';

type Tab = 'terminal' | 'editor' | 'bench';

export function App() {
  const [tasks, setTasks] = useState<TaskSummary[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('terminal');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    request('tasks.list', {})
      .then((t) => {
        setTasks(t);
        setSelected(t[0]?.id ?? null);
      })
      .catch((e: Error) => setError(e.message));
  }, []);

  const task = tasks.find((t) => t.id === selected);

  return (
    <div className="app">
      <aside className="tasks">
        <h2>Tasks</h2>
        {error && <p className="error">{error}</p>}
        <ul>
          {tasks.map((t) => (
            <li key={t.id} className={t.id === selected ? 'selected' : ''} onClick={() => setSelected(t.id)}>
              <span className={`state state-${t.state}`}>{t.state}</span>
              <span className="title">{t.title}</span>
              <span className="id">{t.id}</span>
            </li>
          ))}
        </ul>
        {task && <TaskBody body={task.body} />}
      </aside>
      <main>
        <nav className="tabs">
          {(['terminal', 'editor', 'bench'] as const).map((t) => (
            <button key={t} className={t === tab ? 'active' : ''} onClick={() => setTab(t)}>
              {t}
            </button>
          ))}
        </nav>
        <section className="panel" hidden={tab !== 'terminal'}>
          <TerminalPanel visible={tab === 'terminal'} />
        </section>
        <section className="panel" hidden={tab !== 'editor'}>
          <EditorPanel visible={tab === 'editor'} />
        </section>
        <section className="panel" hidden={tab !== 'bench'}>
          <BenchPanel />
        </section>
      </main>
    </div>
  );
}

const checkbox = /^(\s*)- \[( |x)\] (.*?)(?:\s*\{#[\w-]+\})?$/;

function TaskBody({ body }: { body: string }) {
  return (
    <div className="task-body">
      {body.split('\n').map((line, i) => {
        const m = checkbox.exec(line);
        if (m) {
          return (
            <label key={i} style={{ paddingLeft: `${m[1].length * 0.6}em` }}>
              <input type="checkbox" checked={m[2] === 'x'} readOnly /> {m[3]}
            </label>
          );
        }
        if (line.startsWith('## ')) return <h3 key={i}>{line.slice(3)}</h3>;
        return line.trim() ? <p key={i}>{line}</p> : null;
      })}
    </div>
  );
}
