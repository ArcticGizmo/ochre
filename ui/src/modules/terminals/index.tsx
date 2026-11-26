import { useState } from 'react';
import { PathText } from '../../PathText';
import type { ModuleUi, TaskSectionProps } from '../types';
import { NewTerminalDialog } from './NewTerminalDialog';
import { TerminalsView } from './TerminalsView';
import { terminalName, useTerminals } from './useTerminals';

function TerminalsBadge() {
  const terminals = useTerminals();
  return terminals.length > 0 ? <span className="nav-count">{terminals.length}</span> : null;
}

function TaskTerminals({ task, ctx }: TaskSectionProps) {
  const terminals = useTerminals().filter((t) => t.taskId === task.id);
  const [opening, setOpening] = useState(false);
  return (
    <section>
      <h3>Terminals</h3>
      {terminals.length === 0 ? (
        <p className="empty">No terminals.</p>
      ) : (
        <ul className="plain agents">
          {terminals.map((t) => (
            <li key={t.id}>
              <span className={t.running ? 'status status-idle' : 'status status-dormant'}>
                <span className="dot" />
                {t.running ? 'running' : `exited ${t.exitCode}`}
              </span>
              <span className="who" title={t.folder}>
                {terminalName(t)}
              </span>
              <PathText path={t.folder} />
              {terminalName(t) !== t.profileName && <span className="muted">{t.profileName}</span>}
              <button className="link" onClick={() => ctx.showView('terminals', t.id)}>
                Open
              </button>
            </li>
          ))}
        </ul>
      )}
      <button className="start-agent" onClick={() => setOpening(true)}>
        Open terminal
      </button>
      <NewTerminalDialog
        open={opening}
        taskId={task.id}
        tasks={ctx.tasks}
        onClose={() => setOpening(false)}
        onOpened={(id) => {
          setOpening(false);
          ctx.showView('terminals', id);
        }}
      />
    </section>
  );
}

export const terminalsUi: ModuleUi = {
  id: 'terminals',
  view: {
    key: 'terminals',
    label: 'Terminals',
    Badge: TerminalsBadge,
    render: ({ param, setParam, ctx }) => <TerminalsView selected={param} onSelect={setParam} ctx={ctx} />,
  },
  TaskSection: TaskTerminals,
};
