import { useEffect, useState } from 'react';
import { request, type SessionSummary } from '../bridge';
import { Dialog } from '../Dialog';

interface Props {
  open: boolean;
  taskId: string;
  title: string;
  /** The task's agents that are running now; each is asked for a note, then stopped. */
  running: SessionSummary[];
  onClose: () => void;
  onParked: () => void;
}

/** Parks a task: your note now, each running agent's note as it arrives, and the agents go dormant. */
export function ParkDialog({ open, taskId, title, running, onClose, onParked }: Props) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setNote('');
      setError(null);
    }
  }, [open]);

  async function park() {
    setBusy(true);
    setError(null);
    try {
      await request('tasks.park', { id: taskId, note: note.trim() || null });
      onParked();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open={open}
      title={`Park ${taskId}?`}
      onClose={onClose}
      onSubmit={park}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy}>
            Park task
          </button>
        </>
      }
    >
      <p>
        <strong>{title}</strong> moves to Parked.
      </p>
      {running.length === 1 ? (
        <p>
          Its running agent ({running[0].role ?? running[0].id.slice(0, 8)}) will be asked where it is and what comes next, then stopped. It stays on the task, dormant, and picks up where it left off when you send it a message.
        </p>
      ) : running.length > 1 ? (
        <p>
          Its {running.length} running agents ({running.map((s) => s.role ?? s.id.slice(0, 8)).join(', ')}) will each be asked where they are and what comes next, then stopped. They stay on the task, dormant, and pick up where they left off when you send them a message.
        </p>
      ) : (
        <p>No agents are running on it.</p>
      )}
      <label className="field">
        <span>Your note (optional)</span>
        <textarea rows={4} autoFocus value={note} onChange={(e) => setNote(e.target.value)} placeholder="Where you are and what to do first next time" />
      </label>
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
