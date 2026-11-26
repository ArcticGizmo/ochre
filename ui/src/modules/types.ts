import type { ComponentType, ReactNode } from 'react';
import type { TaskDetail, TaskListItem } from '../bridge';

/**
 * What the app offers a module's UI (ADR 0012). Contributions navigate through this rather than reaching into the
 * app or another module.
 */
export interface AppContext {
  tasks: TaskListItem[];
  openTask: (taskId: string) => void;
  /** Shows an agent's whole thread beside its task. */
  openThread: (sessionId: string, taskId: string) => void;
  /**
   * The agents a module view has on screen, so their finished turns count as seen and notifications stay quiet about
   * them. Only heard while that view is showing.
   */
  reportViewing: (viewKey: string, sessionIds: string[]) => void;
  /** Shows the task in a window of its own on this desktop (asking first if one on another desktop shows it). */
  openInWindow: (taskId: string) => void;
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

/** A folder the task works in: one of its repos or worktrees. */
export interface FolderStatusProps {
  taskId: string;
  path: string;
  /** A worktree made for the task, rather than one of its repos. */
  worktree?: boolean;
  /** Given where the folder may offer actions that navigate (the task's own page); absent on Today. */
  ctx?: AppContext;
}

/** The running modules' folder statuses, in module order, as the app hands them to its views. */
export type FolderStatuses = { id: string; Status: ComponentType<FolderStatusProps> }[];

/** One module's contributions to the UI. Ids match the host's module ids. */
export interface ModuleUi {
  id: string;
  view?: ModuleView;
  /** A section of the task detail, below Agents. */
  TaskSection?: ComponentType<TaskSectionProps>;
  /** Drawn beside the Agents heading on a task (e.g. a button that shows them in a grid). */
  AgentsAction?: ComponentType<TaskSectionProps>;
  /** Drawn after each of a task's folders, on the task and on Today (e.g. its branch and changes). */
  FolderStatus?: ComponentType<FolderStatusProps>;
}
