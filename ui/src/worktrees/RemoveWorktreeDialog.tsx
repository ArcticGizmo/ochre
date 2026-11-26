import { useEffect, useState } from 'react';
import { request, type WorktreeCheckDto, type WorktreeDto } from '../bridge';
import { Dialog } from '../Dialog';
import { PathText } from '../PathText';

interface Props {
  taskId: string;
  worktree: WorktreeDto | null;
  onClose: () => void;
}

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/**
 * Removes a task's worktree, saying first what goes with it: uncommitted changes need their own tick, and the
 * branch is only deleted when asked, unticked by default when commits exist nowhere else.
 */
export function RemoveWorktreeDialog({ taskId, worktree, onClose }: Props) {
  const [check, setCheck] = useState<WorktreeCheckDto | null>(null);
  const [discard, setDiscard] = useState(false);
  const [deleteBranch, setDeleteBranch] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Keyed on the path: the task reloads while the dialog is open (an agent ticking the plan), and a new object for
  // the same worktree must not reset the ticks.
  const path = worktree?.path ?? null;
  useEffect(() => {
    if (!path) return;
    setCheck(null);
    setDiscard(false);
    setDeleteBranch(false);
    setError(null);
    let alive = true;
    request('worktrees.check', { taskId, path })
      .then((c) => {
        if (!alive) return;
        setCheck(c);
        setDeleteBranch(c.branch !== null && c.unpushedCommits === 0);
      })
      .catch((e: Error) => alive && setError(e.message));
    return () => {
      alive = false;
    };
  }, [taskId, path]);

  const blocked = !check || !check.providerRunning || check.runningAgents > 0 || (check.changed > 0 && (!check.canDiscardChanges || !discard));

  async function remove() {
    if (!worktree || !check || blocked) return;
    setBusy(true);
    setError(null);
    try {
      await request('worktrees.remove', { taskId, path: worktree.path, discardChanges: check.changed > 0 && discard, deleteBranch: deleteBranch && check.branch !== null });
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open={worktree !== null}
      title={check?.providerRunning ? `${check.removeLabel}?` : 'Remove this worktree?'}
      onClose={onClose}
      onSubmit={remove}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="danger" disabled={busy || blocked}>
            {check?.providerRunning ? check.removeLabel : 'Remove worktree'}
          </button>
        </>
      }
    >
      {worktree && (
        <p>
          <PathText path={worktree.path} />
        </p>
      )}
      {!check && !error && <p className="muted">Checking what's in it...</p>}
      {check && !check.providerRunning && <p>It was made by {worktree?.provider}, which isn't running. Turn its module on in Settings to remove it from here.</p>}
      {check && check.runningAgents > 0 && <p className="error">{plural(check.runningAgents, 'agent')} working in this folder. Stop them first.</p>}
      {check && check.providerRunning && (
        <>
          <p>{check.exists ? check.removeDescription : 'There is nothing of the task left in it; this tidies up and takes it off the task.'}</p>
          {check.changed > 0 && !check.canDiscardChanges && (
            <p className="error">{plural(check.changed, 'uncommitted change')} in it. Commit or stash them first.</p>
          )}
          {check.changed > 0 && check.canDiscardChanges && (
            <label className="check-field">
              <input type="checkbox" checked={discard} onChange={(e) => setDiscard(e.target.checked)} />
              <span>
                Discard {plural(check.changed, 'uncommitted change')}
                <small>They exist only in this folder and can't be got back.</small>
              </span>
            </label>
          )}
          {check.branch !== null && (
            <label className="check-field">
              <input type="checkbox" checked={deleteBranch} onChange={(e) => setDeleteBranch(e.target.checked)} />
              <span>
                Also delete the branch <code>{check.branch}</code>
                <small className={check.unpushedCommits === 0 ? undefined : 'warn'}>
                  {check.unpushedCommits === null
                    ? "Ochre couldn't tell whether it has commits that exist nowhere else."
                    : check.unpushedCommits === 0
                      ? 'Every commit on it is also on another branch or a remote.'
                      : `${plural(check.unpushedCommits, 'commit')} on it ${check.unpushedCommits === 1 ? 'exists' : 'exist'} on no other branch or remote, and would be lost.`}
                </small>
              </span>
            </label>
          )}
        </>
      )}
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
