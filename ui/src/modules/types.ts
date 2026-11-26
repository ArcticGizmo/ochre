import type { ComponentType, ReactNode } from 'react';
import type { TaskDetail, TaskListItem } from '../bridge';

/**
 * What the app offers a module's UI (ADR 0012). Contributions navigate through this rather than reaching into the
 * app or another module.
 */
export interface AppContext {
  tasks: TaskListItem[];
  openTask: (taskId: string) => void;
  /** Shows a view in the title bar, with a parameter it understands (e.g. which terminal tab). */
  showView: (key: string, param?: string | null) => void;
  onError: (message: string) => void;
}

/** A title-bar view. `param` is per window, so two windows can each sit on their own tab. */
export interface ModuleView {
  key: string;
  label: string;
  /** Drawn after the label, e.g. a count. */
  Badge?: ComponentType;
  render: (props: { param: string | null; setParam: (param: string | null) => void; ctx: AppContext }) => ReactNode;
}

export interface TaskSectionProps {
  task: TaskDetail;
  ctx: AppContext;
}

/** One module's contributions to the UI. Ids match the host's module ids. */
export interface ModuleUi {
  id: string;
  view?: ModuleView;
  /** A section of the task detail, below Agents. */
  TaskSection?: ComponentType<TaskSectionProps>;
}
