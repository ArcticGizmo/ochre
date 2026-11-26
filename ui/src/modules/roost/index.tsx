import type { ModuleUi, TaskSectionProps } from '../types';
import { roostViewKey, RoostView } from './RoostView';

/** Beside a task's Agents heading: shows them in the Grid view, in this window. */
function GridButton({ task, ctx }: TaskSectionProps) {
  if (task.sessions.length === 0) return null;
  return (
    <button className="grid-link" title="This task's agents side by side" onClick={() => ctx.showView(roostViewKey, task.id)}>
      <GridIcon />
      Grid
    </button>
  );
}

function GridIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">
      <rect x="1.5" y="1.5" width="4.5" height="4.5" fill="none" stroke="currentColor" strokeWidth="1.3" />
      <rect x="8" y="1.5" width="4.5" height="4.5" fill="none" stroke="currentColor" strokeWidth="1.3" />
      <rect x="1.5" y="8" width="4.5" height="4.5" fill="none" stroke="currentColor" strokeWidth="1.3" />
      <rect x="8" y="8" width="4.5" height="4.5" fill="none" stroke="currentColor" strokeWidth="1.3" />
    </svg>
  );
}

export const roostUi: ModuleUi = {
  id: 'roost',
  view: {
    key: roostViewKey,
    label: 'Grid',
    render: ({ param, setParam, ctx }) => <RoostView taskId={param} setTaskId={setParam} ctx={ctx} />,
  },
  AgentsAction: GridButton,
};
