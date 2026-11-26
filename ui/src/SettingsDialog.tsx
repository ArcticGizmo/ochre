import { useEffect, useState } from 'react';
import { request, type ModuleDto } from './bridge';
import { Dialog } from './Dialog';

interface Props {
  open: boolean;
  onClose: () => void;
}

/**
 * Modules on and off (ADR 0012). A change applies when Ochre next starts, so after saving one the dialog offers to
 * restart now.
 */
export function SettingsDialog({ open, onClose }: Props) {
  const [modules, setModules] = useState<ModuleDto[]>([]);
  const [choices, setChoices] = useState<Record<string, boolean>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    request('settings.get', {})
      .then((s) => {
        setModules(s.modules);
        setChoices(Object.fromEntries(s.modules.map((m) => [m.id, m.enabled])));
      })
      .catch((e: Error) => setError(e.message));
  }, [open]);

  // Saved choices that differ from what's running now; they wait for a restart.
  const pending = modules.filter((m) => m.enabled !== m.running && !m.error);
  const unsaved = modules.some((m) => choices[m.id] !== m.enabled);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      const s = await request('settings.update', { modules: modules.map((m) => ({ id: m.id, enabled: choices[m.id] ?? m.enabled })) });
      setModules(s.modules);
      if (!s.modules.some((m) => m.enabled !== m.running)) onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function restart() {
    setBusy(true);
    try {
      await request('app.restart', {});
    } catch (e) {
      setError((e as Error).message);
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
            {unsaved ? 'Cancel' : 'Close'}
          </button>
          {pending.length > 0 && !unsaved ? (
            <button type="button" className="primary" disabled={busy} onClick={restart}>
              Restart Ochre now
            </button>
          ) : (
            <button type="submit" className="primary" disabled={busy || !unsaved}>
              Save
            </button>
          )}
        </>
      }
    >
      <h3 className="settings-heading">Modules</h3>
      <p className="muted settings-note">Tasks, agents, parking and Today always work; modules add to them. Changes apply when Ochre restarts.</p>
      {modules.map((m) => (
        <label key={m.id} className="check-field">
          <input type="checkbox" checked={choices[m.id] ?? m.enabled} onChange={(e) => setChoices((c) => ({ ...c, [m.id]: e.target.checked }))} />
          <span>
            {m.name}
            {m.error ? (
              <span className="error"> · failed to start: {m.error}</span>
            ) : m.enabled !== m.running ? (
              <span className="muted"> · {m.enabled ? 'on' : 'off'} after a restart</span>
            ) : null}
            <small>{m.description}</small>
          </span>
        </label>
      ))}
      {pending.length > 0 && !unsaved && (
        <p className="muted">
          Restarting closes and reopens Ochre. Agents come back dormant;{' '}
          {pending.some((m) => m.id === 'terminals' && !m.enabled)
            ? "running terminals end; their tabs are kept and come back if you turn Terminals on again."
            : 'terminals come back with their scrollback.'}
        </p>
      )}
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
