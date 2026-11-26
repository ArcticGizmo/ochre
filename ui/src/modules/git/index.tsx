import type { FolderStatusProps, ModuleUi } from '../types';
import { useGitStatus } from './useGitStatus';

const samePath = (a: string, b: string) => a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase();

/** The folder's branch, commits ahead and behind its upstream, and uncommitted changes. Nothing for a plain folder. */
function GitFolderStatus({ taskId, path }: FolderStatusProps) {
  const status = useGitStatus(taskId)?.find((s) => samePath(s.path, path));
  if (!status || status.missing || !status.isRepo) return null;

  const branch = status.branch ?? 'detached';
  const tracking = status.upstream ? `${status.ahead} ahead and ${status.behind} behind ${status.upstream}` : 'no upstream branch';
  const changes = status.changed === 0 ? 'no uncommitted changes' : `${status.changed} uncommitted change${status.changed === 1 ? '' : 's'}`;
  return (
    <span className="git-status" title={`${branch}: ${tracking}, ${changes}`}>
      <span className="git-branch">{branch}</span>
      {status.ahead > 0 && <span className="git-ahead">↑{status.ahead}</span>}
      {status.behind > 0 && <span className="git-behind">↓{status.behind}</span>}
      {status.changed > 0 ? <span className="git-dirty">{status.changed} changed</span> : <span className="git-clean">clean</span>}
    </span>
  );
}

export const gitUi: ModuleUi = {
  id: 'git',
  FolderStatus: GitFolderStatus,
};
