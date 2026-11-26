import { useState } from 'react';
import type { WorktreeDto } from '../bridge';
import type { FolderStatuses } from '../modules';
import { PathText } from '../PathText';
import { RemoveWorktreeDialog } from './RemoveWorktreeDialog';
import { useWorktreeProviders } from './useWorktreeProviders';

/** What the running modules say about one folder (its git branch and changes, say); nothing with them all off. */
export function FolderStatus({ statuses, taskId, path }: { statuses: FolderStatuses; taskId: string; path: string }) {
  return (
    <>
      {statuses.map(({ id, Status }) => (
        <Status key={id} taskId={taskId} path={path} />
      ))}
    </>
  );
}

interface Props {
  taskId: string;
  repos: string[];
  worktrees: WorktreeDto[];
  statuses: FolderStatuses;
  onSaveRepos: (repos: string[]) => void;
}

/** The task's repos (editable) and the worktrees made for it, each with what the modules know about it. */
export function TaskFolders({ taskId, repos, worktrees, statuses, onSaveRepos }: Props) {
  const [path, setPath] = useState('');
  const [removing, setRemoving] = useState<WorktreeDto | null>(null);
  const providers = useWorktreeProviders();

  return (
    <div className="repos">
      {repos.length === 0 && <p className="empty">No repos. Agents need one to start in.</p>}
      <ul className="plain">
        {repos.map((r) => (
          <li key={r}>
            <PathText path={r} />
            <FolderStatus statuses={statuses} taskId={taskId} path={r} />
            <button className="link" onClick={() => onSaveRepos(repos.filter((x) => x !== r))}>
              Remove
            </button>
          </li>
        ))}
        {worktrees.map((w) => (
          <li key={w.path}>
            <span className="kind" title={`Made by ${w.provider} from ${w.repo}`}>
              worktree
            </span>
            <PathText path={w.path} />
            {w.exists ? <FolderStatus statuses={statuses} taskId={taskId} path={w.path} /> : <span className="muted">folder gone</span>}
            {/* With the module that made it off, it's a plain folder: show the branch it was made on. */}
            {!providers.some((p) => p.id === w.provider) && w.branch && <code className="muted">{w.branch}</code>}
            {providers.some((p) => p.id === w.provider) && (
              <button className="link" onClick={() => setRemoving(w)}>
                Remove...
              </button>
            )}
          </li>
        ))}
      </ul>
      <form
        className="step-input"
        onSubmit={(e) => {
          e.preventDefault();
          const p = path.trim();
          if (!p) return;
          onSaveRepos([...repos, p]);
          setPath('');
        }}
      >
        <input value={path} onChange={(e) => setPath(e.target.value)} placeholder="C:\path\to\repo" />
      </form>
      <RemoveWorktreeDialog taskId={taskId} worktree={removing} onClose={() => setRemoving(null)} />
    </div>
  );
}
