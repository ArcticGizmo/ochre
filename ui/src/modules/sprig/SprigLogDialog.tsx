import { useCallback, useEffect, useRef, useState } from 'react';
import { request, type SprigLogDto } from '../../bridge';
import { Dialog } from '../../Dialog';
import { PathText } from '../../PathText';

interface Props {
  taskId: string;
  workspaceKey: string;
  /** The app (<c>repo/app</c>) whose log to show, or null when closed. */
  app: string | null;
  onClose: () => void;
}

/** The end of an app's log, as sprig's host writes it. Refreshed on demand, scrolled to the newest line. */
export function SprigLogDialog({ taskId, workspaceKey, app, onClose }: Props) {
  const [log, setLog] = useState<SprigLogDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pre = useRef<HTMLPreElement>(null);

  const load = useCallback(() => {
    if (!app) return;
    request('sprig.log', { taskId, key: workspaceKey, app })
      .then((l) => {
        setLog(l);
        setError(null);
      })
      .catch((e: Error) => setError(e.message));
  }, [taskId, workspaceKey, app]);

  useEffect(() => {
    setLog(null);
    setError(null);
    load();
  }, [load]);

  useEffect(() => {
    if (pre.current) pre.current.scrollTop = pre.current.scrollHeight;
  }, [log]);

  return (
    <Dialog
      open={app !== null}
      title={`Log: ${app ?? ''}`}
      onClose={onClose}
      actions={
        <>
          <button type="button" onClick={load}>
            Refresh
          </button>
          <button type="button" className="primary" onClick={onClose}>
            Close
          </button>
        </>
      }
    >
      {log && (
        <p className="muted small">
          <PathText path={log.path} />
          {log.truncated && ' · the last lines only'}
        </p>
      )}
      {error && <p className="error">{error}</p>}
      {!log && !error && <p className="muted">Reading the log...</p>}
      {log && (
        <pre ref={pre} className="sprig-log">
          {log.text || '(empty)'}
        </pre>
      )}
    </Dialog>
  );
}
