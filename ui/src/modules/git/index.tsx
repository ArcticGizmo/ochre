import type { FolderStatusProps, ModuleUi } from '../types';
import { graphParam, GraphView } from './GraphView';
import { useGitStatus } from './useGitStatus';

const samePath = (a: string, b: string) => a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase();

/**
 * The folder's branch, commits ahead and behind its upstream, and uncommitted changes, and on the task's own page a
 * link to its graph (a repo's whole history; a worktree's branch against its base). Nothing for a plain folder.
 */
function GitFolderStatus({ taskId, path, worktree, ctx }: FolderStatusProps) {
  const status = useGitStatus(taskId)?.find((s) => samePath(s.path, path));
  if (!status || status.missing || !status.isRepo) return null;

  const branch = status.branch ?? 'detached';
  const tracking = status.upstream ? `${status.ahead} ahead and ${status.behind} behind ${status.upstream}` : 'no upstream branch';
  const changes = status.changed === 0 ? 'no uncommitted changes' : `${status.changed} uncommitted change${status.changed === 1 ? '' : 's'}`;
  return (
    <>
      <span className="git-status" title={`${branch}: ${tracking}, ${changes}`}>
        <span className="git-branch">{branch}</span>
        {status.ahead > 0 && <span className="git-ahead">↑{status.ahead}</span>}
        {status.behind > 0 && <span className="git-behind">↓{status.behind}</span>}
        {status.changed > 0 ? <span className="git-dirty">{status.changed} changed</span> : <span className="git-clean">clean</span>}
      </span>
      {ctx && (
        <button
          className="link git-graph-link"
          title={worktree ? 'This branch against the one it was made from' : "The repo's branches and history"}
          onClick={() => ctx.showView('git', graphParam({ task: taskId, folder: path, scope: worktree ? 'branch' : 'all' }))}
        >
          <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">
            <circle cx="4" cy="3" r="1.6" fill="none" stroke="currentColor" strokeWidth="1.3" />
            <circle cx="4" cy="11" r="1.6" fill="none" stroke="currentColor" strokeWidth="1.3" />
            <circle cx="10" cy="5.5" r="1.6" fill="none" stroke="currentColor" strokeWidth="1.3" />
            <path d="M4 4.6v4.8M10 7.1c0 2-2.5 2.2-5 3" fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" />
          </svg>
          Graph
        </button>
      )}
    </>
  );
}

export const gitUi: ModuleUi = {
  id: 'git',
  view: {
    key: 'git',
    label: 'Git',
    render: ({ param, setParam, ctx }) => <GraphView param={param} setParam={setParam} ctx={ctx} />,
  },
  FolderStatus: GitFolderStatus,
};
