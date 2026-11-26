import { useEffect, useState } from 'react';
import { on, request, type UpdateDto } from '../bridge';
import { ConfirmDialog } from '../Dialog';
import { when } from '../tasks/present';

/** Whether an update is waiting or running, as the host last said; every window follows the same broadcasts. */
export function useUpdate(): UpdateDto | null {
  const [update, setUpdate] = useState<UpdateDto | null>(null);
  useEffect(() => {
    request('app.update', {})
      .then(setUpdate)
      .catch(() => {});
    return on('app.updateChanged', setUpdate);
  }, []);
  return update;
}

const restartNote = 'Ochre closes and starts again on the new version. Agents come back dormant (sending to one resumes it), and terminals come back with their scrollback.';

/** The title bar's version, which becomes a button while a newer one is waiting. */
export function UpdateBadge() {
  const update = useUpdate();
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (!update) return null;

  if (update.state === 'downloading' || update.state === 'restarting') {
    return (
      <span className="version update-progress" role="status">
        {update.state === 'restarting' ? 'Restarting to update…' : `Downloading v${update.available} ${update.percent ?? 0}%`}
      </span>
    );
  }
  if (update.state !== 'available' || !update.available) return <span className="version">v{update.current}</span>;

  async function apply() {
    setError(null);
    try {
      const after = await request('app.applyUpdate', {});
      // Still here: the download or hand-over failed, and the reason is in the state.
      if (after.error) setError(after.error);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <>
      <button className="update-badge" title={`You have v${update.current}`} onClick={() => setConfirming(true)}>
        Update to v{update.available}
      </button>
      <ConfirmDialog
        open={confirming}
        title={`Update Ochre to v${update.available}?`}
        confirmLabel="Update and restart"
        onConfirm={() => {
          setConfirming(false);
          void apply();
        }}
        onClose={() => setConfirming(false)}
      >
        <p>{restartNote}</p>
      </ConfirmDialog>
      {error && (
        <span className="error update-error" title={error}>
          Update failed
        </span>
      )}
    </>
  );
}

/** Settings' Updates section: the version, the last check, and a button to check now. */
export function UpdateSettings() {
  const update = useUpdate();
  const [checking, setChecking] = useState(false);
  if (!update) return null;

  if (update.state === 'unsupported') {
    return <p className="muted settings-note">Version {update.current}. This copy isn't installed, so it doesn't update itself.</p>;
  }

  async function check() {
    setChecking(true);
    try {
      await request('app.checkForUpdate', {});
    } finally {
      setChecking(false);
    }
  }

  const status =
    update.state === 'checking'
      ? 'Checking…'
      : update.state === 'available'
        ? `Version ${update.available} is ready to install from the title bar.`
        : update.state === 'downloading'
          ? `Downloading version ${update.available}: ${update.percent ?? 0}%`
          : update.state === 'restarting'
            ? 'Restarting to update…'
            : update.state === 'upToDate'
              ? `Up to date${update.checkedAt ? `, checked ${when(update.checkedAt)}` : ''}.`
              : update.state === 'error'
                ? `Couldn't check for updates: ${update.error}`
                : 'Ochre checks for updates a minute after it starts and every six hours.';

  return (
    <div className="update-settings">
      <p className="settings-note">
        Version {update.current}. <span className={update.state === 'error' ? 'error' : 'muted'}>{status}</span>
      </p>
      <button type="button" disabled={checking || update.state === 'checking' || update.state === 'downloading' || update.state === 'restarting'} onClick={() => void check()}>
        Check for updates
      </button>
    </div>
  );
}
