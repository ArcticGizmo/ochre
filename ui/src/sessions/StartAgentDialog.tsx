import { useEffect, useState } from 'react';
import { request } from '../bridge';
import { Dialog } from '../Dialog';
import { PathText } from '../PathText';

interface Props {
  open: boolean;
  taskId: string;
  repos: string[];
  onClose: () => void;
  onStarted: (sessionId: string) => void;
}

const OTHER = '__other__';
const DEFAULT_PROMPT = 'Read the task, then start on the next unchecked plan item. Tick items as you finish them.';

const modes: { value: string; label: string }[] = [
  { value: 'default', label: 'Ask before edits and commands' },
  { value: 'acceptEdits', label: 'Accept edits, ask before commands' },
  { value: 'plan', label: 'Plan first' },
  { value: 'auto', label: 'Auto' },
];

/** Launches a controlled Claude session bound to the task. Asks about folder trust when Claude Code would have. */
export function StartAgentDialog({ open, taskId, repos, onClose, onStarted }: Props) {
  const [folder, setFolder] = useState(repos[0] ?? OTHER);
  const [otherFolder, setOtherFolder] = useState('');
  const [role, setRole] = useState('');
  const [mode, setMode] = useState('default');
  const [prompt, setPrompt] = useState(DEFAULT_PROMPT);
  const [askTrust, setAskTrust] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Reset only when the dialog opens: the task reloads whenever an agent ticks the plan, and a new repos array then
  // must not wipe what's being typed.
  const firstRepo = repos[0] ?? OTHER;
  useEffect(() => {
    if (open) {
      setFolder(firstRepo);
      setOtherFolder('');
      setRole('');
      setMode('default');
      setPrompt(DEFAULT_PROMPT);
      setAskTrust(false);
      setError(null);
    }
  }, [open]);

  const cwd = folder === OTHER ? otherFolder.trim() : folder;

  async function start(trustFolder: boolean) {
    if (!cwd) return;
    setBusy(true);
    setError(null);
    try {
      const result = await request('sessions.start', {
        taskId,
        cwd,
        role: role.trim() || null,
        permissionMode: mode,
        firstPrompt: prompt.trim() || null,
        trustFolder,
      });
      if (result.needsTrust) setAskTrust(true);
      else if (result.sessionId) onStarted(result.sessionId);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (askTrust) {
    return (
      <Dialog
        open={open}
        title="Trust this folder?"
        onClose={onClose}
        onSubmit={() => start(true)}
        actions={
          <>
            <button type="button" onClick={() => setAskTrust(false)}>
              Back
            </button>
            <button type="submit" className="primary" disabled={busy} autoFocus>
              Trust and start
            </button>
          </>
        }
      >
        <p>
          Claude Code hasn't been told to trust <PathText path={cwd} />. It normally asks before working in a new folder, but not for sessions Ochre starts, so Ochre asks instead.
        </p>
        <p>Only trust folders whose files you'd let Claude read and run. Your answer is saved where Claude Code keeps its own, so it won't ask again for this folder.</p>
        {error && <p className="error">{error}</p>}
      </Dialog>
    );
  }

  return (
    <Dialog
      open={open}
      title="Start an agent"
      onClose={onClose}
      onSubmit={() => start(false)}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="primary" disabled={busy || !cwd}>
            Start agent
          </button>
        </>
      }
    >
      <label className="field">
        <span>Folder</span>
        <select value={folder} onChange={(e) => setFolder(e.target.value)}>
          {repos.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
          <option value={OTHER}>Another folder...</option>
        </select>
      </label>
      {folder === OTHER && (
        <label className="field">
          <span>Folder path</span>
          <input autoFocus value={otherFolder} onChange={(e) => setOtherFolder(e.target.value)} placeholder="C:\path\to\folder" />
        </label>
      )}
      <label className="field">
        <span>Role (optional)</span>
        <input value={role} maxLength={40} onChange={(e) => setRole(e.target.value)} placeholder="e.g. implement, review, tests" />
      </label>
      <label className="field">
        <span>Permissions</span>
        <select value={mode} onChange={(e) => setMode(e.target.value)}>
          {modes.map((m) => (
            <option key={m.value} value={m.value}>
              {m.label}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>First message</span>
        <textarea rows={3} value={prompt} onChange={(e) => setPrompt(e.target.value)} placeholder="Leave empty to write it in the thread" />
      </label>
      {error && <p className="error">{error}</p>}
    </Dialog>
  );
}
