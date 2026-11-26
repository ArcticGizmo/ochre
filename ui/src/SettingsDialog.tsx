import { useEffect, useState } from 'react';
import { request, type ModuleDto } from './bridge';
import { Dialog } from './Dialog';
import { UpdateSettings } from './updates/Updates';

interface Props {
  open: boolean;
  onClose: () => void;
}

/**
 * Modules on and off (ADR 0012), the jump-to-task shortcut, starting at sign-in and updates. A module change applies
 * when Ochre next starts, so after saving one the dialog offers to restart now; the rest apply at once.
 */
export function SettingsDialog({ open, onClose }: Props) {
  const [modules, setModules] = useState<ModuleDto[]>([]);
  const [choices, setChoices] = useState<Record<string, boolean>>({});
  const [hotkey, setHotkey] = useState({ saved: '', draft: '', error: null as string | null });
  // Null where it isn't offered (a copy that isn't installed).
  const [signIn, setSignIn] = useState({ saved: null as boolean | null, draft: null as boolean | null });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    request('settings.get', {})
      .then((s) => {
        setModules(s.modules);
        setChoices(Object.fromEntries(s.modules.map((m) => [m.id, m.enabled])));
        setHotkey({ saved: s.jumpHotkey, draft: s.jumpHotkey, error: s.jumpHotkeyError });
        setSignIn({ saved: s.startAtSignIn, draft: s.startAtSignIn });
      })
      .catch((e: Error) => setError(e.message));
  }, [open]);

  // Saved choices that differ from what's running now; they wait for a restart.
  const pending = modules.filter((m) => m.enabled !== m.running && !m.error);
  const hotkeyChanged = hotkey.draft.trim() !== hotkey.saved;
  const signInChanged = signIn.draft !== signIn.saved;
  const unsaved = modules.some((m) => choices[m.id] !== m.enabled) || hotkeyChanged || signInChanged;

  async function save() {
    setBusy(true);
    setError(null);
    try {
      const s = await request('settings.update', {
        modules: modules.map((m) => ({ id: m.id, enabled: choices[m.id] ?? m.enabled })),
        jumpHotkey: hotkeyChanged ? hotkey.draft.trim() : null,
        startAtSignIn: signInChanged ? signIn.draft : null,
      });
      setModules(s.modules);
      setHotkey({ saved: s.jumpHotkey, draft: s.jumpHotkey, error: s.jumpHotkeyError });
      setSignIn({ saved: s.startAtSignIn, draft: s.startAtSignIn });
      if (!s.modules.some((m) => m.enabled !== m.running) && !s.jumpHotkeyError) onClose();
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
      <h3 className="settings-heading">Jump to task</h3>
      <p className="muted settings-note">
        A shortcut that works from any app: it brings up an Ochre window on the desktop you're on and asks which task to go to. Inside Ochre, Ctrl+K does the same. Leave it empty to turn it off.
      </p>
      <label className="settings-field">
        <span>Shortcut</span>
        <input
          value={hotkey.draft}
          placeholder="Off"
          maxLength={64}
          spellCheck={false}
          onChange={(e) => setHotkey((h) => ({ ...h, draft: e.target.value }))}
        />
      </label>
      {hotkey.error && !hotkeyChanged && <p className="error">{hotkey.error}</p>}
      <h3 className="settings-heading">Ochre</h3>
      {signIn.draft !== null && (
        <label className="check-field">
          <input type="checkbox" checked={signIn.draft} onChange={(e) => setSignIn((s) => ({ ...s, draft: e.target.checked }))} />
          <span>
            Start Ochre when you sign in
            <small>In the tray, with no window, so agents and terminals are there when you need them.</small>
          </span>
        </label>
      )}
      <UpdateSettings />
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
