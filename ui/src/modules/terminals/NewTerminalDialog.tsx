import { useEffect, useState } from 'react';
import { request, type ShellProfileDto, type TaskDetail, type TaskListItem } from '../../bridge';
import { Dialog } from '../../Dialog';
import { useFolderChoice } from '../../worktrees/useFolderChoice';

interface Props {
  open: boolean;
  /** The task to preselect (opened from a task), or null. */
  taskId: string | null;
  tasks: TaskListItem[];
  onClose: () => void;
  onOpened: (terminalId: string) => void;
}

const NONE = '';

/** Opens a terminal tab: a shell the host found, in a folder (the task's repos first), optionally on a task. */
export function NewTerminalDialog({ open, taskId, tasks, onClose, onOpened }: Props) {
  const [profiles, setProfiles] = useState<ShellProfileDto[]>([]);
  const [task, setTask] = useState(NONE);
  const [detail, setDetail] = useState<TaskDetail | null>(null);
  const choice = useFolderChoice({
    taskId: detail?.id ?? null,
    title: detail?.title ?? '',
    repos: detail?.repos ?? [],
    worktrees: detail?.worktrees ?? [],
    otherMayBeEmpty: true,
    otherPlaceholder: 'Empty for your home folder, or C:\\path\\to\\folder',
  });
  const [profile, setProfile] = useState('');
  const [command, setCommand] = useState('');
  const [keep, setKeep] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setTask(taskId ?? NONE);
    setCommand('');
    setKeep(true);
    setError(null);
    request('terminals.profiles', {})
      .then((list) => {
        setProfiles(list);
        setProfile((p) => (list.some((x) => x.id === p) ? p : (list[0]?.id ?? '')));
      })
      .catch((e: Error) => setError(e.message));
  }, [open, taskId]);

  // The folder choices follow the task: its repos and worktrees, a new worktree, then any other folder.
  useEffect(() => {
    if (!open) return;
    if (!task) {
      setDetail(null);
      return;
    }
    let alive = true;
    request('tasks.get', { id: task })
      .then((t) => alive && setDetail(t))
      .catch(() => alive && setDetail(null));
    return () => {
      alive = false;
    };
  }, [open, task]);

  // A different task (or none) starts the folder choice afresh; a reload of the same task leaves it alone.
  const detailId = detail?.id ?? null;
  useEffect(() => {
    if (open) choice.reset(null);
  }, [open, detailId]);

  const choosable = tasks.filter((t) => t.state !== 'done' || t.id === task);

  async function submit() {
    if (!profile || !choice.ready) return;
    setBusy(true);
    setError(null);
    try {
      const folder = await choice.resolve();
      const t = await request('terminals.open', { profile, folder, taskId: task || null, startupCommand: command.trim() || null, cols: 120, rows: 30, keepScrollback: keep });
      onOpened(t.id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open={open}
      title="New terminal"
      onClose={onClose}
      onSubmit={submit}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy || !profile || !choice.ready}>
            {busy && choice.isNew ? 'Making worktree...' : 'Open terminal'}
          </button>
        </>
      }
    >
      <label className="field">
        <span>Task</span>
        <select value={task} onChange={(e) => setTask(e.target.value)}>
          <option value={NONE}>None</option>
          {choosable.map((t) => (
            <option key={t.id} value={t.id}>
              {t.id} · {t.title}
            </option>
          ))}
        </select>
      </label>
      {choice.fields}
      <label className="field">
        <span>Shell</span>
        <select value={profile} onChange={(e) => setProfile(e.target.value)}>
          {profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>Startup command (optional)</span>
        <input className="mono" value={command} maxLength={1000} onChange={(e) => setCommand(e.target.value)} placeholder="e.g. pnpm dev" />
        <small className="muted">Typed into the shell once it starts. After Ochre restarts, the tab offers to run it again; it never runs by itself.</small>
      </label>
      <label className="check-field">
        <input type="checkbox" checked={keep} onChange={(e) => setKeep(e.target.checked)} />
        <span>
          Keep scrollback when Ochre quits
          <small>Saved encrypted for your Windows account. Turn off for a tab that will show secrets.</small>
        </span>
      </label>
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
