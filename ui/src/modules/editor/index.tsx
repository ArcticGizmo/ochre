import { ErrorBoundary } from '../../ErrorBoundary';
import type { FolderStatusProps, ModuleUi } from '../types';
import { FilesView, filesParam } from './FilesView';

/** On the task's own page: a link that opens the folder in the Files view. */
function FilesLink({ taskId, path, ctx }: FolderStatusProps) {
  if (!ctx) return null;
  return (
    <button className="link git-graph-link" title="Browse and edit this folder's files" onClick={() => ctx.showView('files', filesParam({ task: taskId, folder: path, at: Date.now() }))}>
      <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">
        <path d="M3 1.5h5l3 3v8H3z" fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinejoin="round" />
        <path d="M8 1.5v3h3" fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinejoin="round" />
      </svg>
      Files
    </button>
  );
}

export const editorUi: ModuleUi = {
  id: 'editor',
  view: {
    key: 'files',
    label: 'Files',
    render: ({ param, setParam, ctx }) => (
      <ErrorBoundary what="the Files view">
        <FilesView param={param} setParam={setParam} ctx={ctx} />
      </ErrorBoundary>
    ),
  },
  FolderStatus: FilesLink,
};
