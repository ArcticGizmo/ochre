import { useEffect, useState, useSyncExternalStore } from 'react';
import { on, request } from '../bridge';
import { Dialog } from '../Dialog';
import { discardAllUnsaved, saveAllUnsaved, subscribeUnsaved, unsavedFiles, unsavedVersion, type SaveFailure } from './unsaved';

/**
 * Tells the host how many unsaved files this window holds, and asks the user what to do with them when the host
 * refuses to close the window (or to quit) because of them. Nothing is lost silently: Cancel keeps the window open.
 */
export function CloseGuard() {
  useSyncExternalStore(subscribeUnsaved, unsavedVersion);
  const files = unsavedFiles();
  const count = files.length;
  const [asking, setAsking] = useState<{ quit: boolean } | null>(null);
  const [busy, setBusy] = useState(false);
  const [failures, setFailures] = useState<SaveFailure[]>([]);

  useEffect(() => {
    request('window.setUnsaved', { count }).catch(() => {});
  }, [count]);

  useEffect(
    () =>
      on('window.closeRequested', (e) => {
        setFailures([]);
        setAsking({ quit: e.quit });
      }),
    [],
  );

  // Saved elsewhere meanwhile (the last tab saved by hand): nothing left to ask about.
  useEffect(() => {
    if (asking && count === 0 && !busy) {
      setAsking(null);
      request('window.close', { quit: asking.quit }).catch(() => {});
    }
  }, [asking, count, busy]);

  const finish = () => {
    const quit = asking?.quit ?? false;
    setAsking(null);
    request('window.close', { quit }).catch(() => {});
  };

  const saveAll = async () => {
    setBusy(true);
    try {
      const failed = await saveAllUnsaved();
      if (failed.length === 0) finish();
      else setFailures(failed);
    } finally {
      setBusy(false);
    }
  };

  const discard = () => {
    discardAllUnsaved();
    finish();
  };

  const n = `${count} unsaved file${count === 1 ? '' : 's'}`;
  return (
    <Dialog
      open={asking !== null && count > 0}
      title={asking?.quit ? `Quit Ochre with ${n}?` : `Close this window with ${n}?`}
      onClose={() => setAsking(null)}
      onSubmit={saveAll}
      actions={
        <>
          <button type="button" onClick={() => setAsking(null)}>
            Cancel
          </button>
          <button type="button" className="danger" onClick={discard} disabled={busy}>
            {asking?.quit ? 'Discard and quit' : 'Discard and close'}
          </button>
          <button type="submit" className="primary" disabled={busy || failures.length > 0} autoFocus>
            {asking?.quit ? 'Save all and quit' : 'Save all and close'}
          </button>
        </>
      }
    >
      <p className="muted">
        {asking?.quit
          ? 'These edits exist only in this window.'
          : 'These edits exist only in this window. Ochre keeps running in the tray, and other windows stay open.'}
      </p>
      <ul className="unsaved-list">
        {files.map((f) => (
          <li key={f.key} title={f.title}>
            <span className="unsaved-dot" aria-hidden="true" />
            {f.name}
            <span className="unsaved-where">{f.detail}</span>
          </li>
        ))}
      </ul>
      {failures.length > 0 && (
        <div className="unsaved-failed" role="alert">
          {failures.map((f) => (
            <p key={f.name}>
              Couldn't save {f.name}: {f.reason}
            </p>
          ))}
          <p className="muted">Cancel and choose in Files, or discard.</p>
        </div>
      )}
    </Dialog>
  );
}
