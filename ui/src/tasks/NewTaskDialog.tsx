import { useState } from 'react';
import { request, type TaskDetail } from '../bridge';
import { Dialog } from '../Dialog';

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (task: TaskDetail) => void;
}

/** Takes everything you'd naturally say about a task at once, and saves it in one write. */
export function NewTaskDialog({ open, onClose, onCreated }: Props) {
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [repos, setRepos] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  function reset() {
    setTitle('');
    setDescription('');
    setRepos('');
    setError(null);
  }

  async function create() {
    if (!title.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const task = await request('tasks.create', {
        title: title.trim(),
        notes: description.trim() || null,
        repos: repos
          .split(/\r?\n/)
          .map((r) => r.trim())
          .filter(Boolean),
      });
      reset();
      onCreated(task);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open={open}
      title="New task"
      onClose={() => {
        reset();
        onClose();
      }}
      onSubmit={create}
      actions={
        <>
          <button
            type="button"
            onClick={() => {
              reset();
              onClose();
            }}
          >
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy || !title.trim()}>
            Create task
          </button>
        </>
      }
    >
      <label className="field">
        <span>Title</span>
        <input autoFocus value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
      </label>
      <label className="field">
        <span>Description</span>
        <textarea
          rows={6}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="What and why. Markdown; links to tickets, PRs and docs are welcome."
        />
      </label>
      <label className="field">
        <span>Repos</span>
        <textarea rows={2} value={repos} onChange={(e) => setRepos(e.target.value)} placeholder={'One absolute path per line, e.g. C:\\git\\my-repo'} />
      </label>
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
