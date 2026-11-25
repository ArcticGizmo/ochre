import { useEffect, useState } from 'react';
import { request } from './bridge';
import { Dialog } from './Dialog';

interface Props {
  open: boolean;
  onClose: () => void;
}

export function SettingsDialog({ open, onClose }: Props) {
  const [autoPark, setAutoPark] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    request('settings.get', {})
      .then((s) => setAutoPark(s.autoParkOnShutdown))
      .catch((e: Error) => setError(e.message));
  }, [open]);

  async function save() {
    setBusy(true);
    try {
      await request('settings.update', { autoParkOnShutdown: autoPark });
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open={open}
      title="Settings"
      onClose={onClose}
      onSubmit={save}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy}>
            Save
          </button>
        </>
      }
    >
      <label className="check-field">
        <input type="checkbox" checked={autoPark} onChange={(e) => setAutoPark(e.target.checked)} />
        <span>
          Park tasks when Windows shuts down
          <small>
            Each task with agents running is parked with an automatic note naming them and the last step ticked. There's no time to ask the agents for notes; they come back dormant and pick up where they were when you send to them. If the shutdown is cancelled, the tasks are put back.
          </small>
        </span>
      </label>
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
