import { useState } from 'react';
import { request, type SprigActResult, type SprigAppDto, type SprigWorkspaceDto } from '../../bridge';
import type { FolderStatusProps, ModuleUi, TaskSectionProps } from '../types';
import { SprigLogDialog } from './SprigLogDialog';
import { useSprigStatus } from './useSprigStatus';

const samePath = (a: string, b: string) => a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase();

/** The word and colour a workspace shows, the way sprig does: blue while starting, red in trouble. */
function look(w: SprigWorkspaceDto): { word: string; tone: string } {
  if (w.activity === 'stopping') return { word: 'stopping', tone: 'busy' };
  if (w.state === 'running') return w.inTrouble ? { word: 'trouble', tone: 'bad' } : { word: 'running', tone: 'ok' };
  if (w.state === 'starting') return { word: 'starting', tone: 'busy' };
  if (w.state === 'failed') return { word: 'failed', tone: 'bad' };
  if (w.state === 'unknown') return { word: w.stateText || 'unknown', tone: 'idle' };
  return { word: w.state, tone: 'idle' };
}

function appTone(a: SprigAppDto): string {
  if (a.state === 'ready' || a.state === 'completed') return 'ok';
  if (a.state === 'exited' || a.state === 'failed' || a.state === 'restarting') return 'bad';
  if (a.state === 'off' || a.state === 'stopped') return 'idle';
  return 'busy';
}

/** Beside a task folder that's in a sprig workspace: its state and how many apps are up. */
function SprigFolderStatus({ taskId, path }: FolderStatusProps) {
  const w = useSprigStatus(taskId)?.workspaces.find((x) => x.folders.some((f) => samePath(f, path)));
  if (!w) return null;
  const { word, tone } = look(w);
  return (
    <span className={`sprig-status tone-${tone}`} title={`Sprig workspace ${w.key}: ${word}${w.of > 0 ? `, ${w.ready} of ${w.of} apps up` : ''}`}>
      <span className="dot" />
      sprig {word}
      {w.of > 0 && (w.state === 'running' || w.state === 'starting') && (
        <span className="muted">
          {' '}
          {w.ready}/{w.of}
        </span>
      )}
    </span>
  );
}

/** The apps of the sprig workspaces the task's folders are in, with start, stop and logs. */
function SprigApps({ task, ctx }: TaskSectionProps) {
  const status = useSprigStatus(task.id);
  const [busy, setBusy] = useState<string | null>(null);
  const [failure, setFailure] = useState<{ key: string; result: SprigActResult } | null>(null);
  const [log, setLog] = useState<{ key: string; app: string } | null>(null);

  if (!status || status.workspaces.length === 0) return null;

  async function act(key: string, action: string, app: string | null) {
    setBusy(`${key}:${action}:${app ?? ''}`);
    setFailure(null);
    try {
      const result = await request('sprig.act', { taskId: task.id, key, action, app });
      if (!result.ok) setFailure({ key, result });
    } catch (e) {
      ctx.onError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  function openInSprig(key: string) {
    request('sprig.open', { taskId: task.id, key })
      .then((r) => !r.ok && setFailure({ key, result: r }))
      .catch((e: Error) => ctx.onError(e.message));
  }

  return (
    <section>
      <h3>Apps</h3>
      {status.feed !== 'live' && (
        <p className="muted">
          {status.feed === 'notInstalled' ? "Sprig isn't installed." : status.feed === 'reconnecting' ? 'Lost touch with sprig; reconnecting. What shows may be out of date.' : 'Connecting to sprig...'}
        </p>
      )}
      {status.feed === 'live' && !status.hostRunning && <p className="muted">Sprig's host isn't running, so no app is. Starting one starts it.</p>}
      {status.workspaces.map((w) => {
        const { word, tone } = look(w);
        const going = w.state === 'running' || w.state === 'starting';
        return (
          <div key={w.key} className="sprig-workspace">
            <div className="sprig-head">
              <span className={`sprig-status tone-${tone}`}>
                <span className="dot" />
                {word}
              </span>
              <code>{w.label}</code>
              <span className="muted small">{w.key}</span>
              <span className="grow" />
              {!going && (
                <button disabled={busy !== null} onClick={() => act(w.key, 'start', null)}>
                  Start apps
                </button>
              )}
              {going && (
                <>
                  <button disabled={busy !== null} onClick={() => act(w.key, 'restart', null)}>
                    Restart
                  </button>
                  <button disabled={busy !== null} onClick={() => act(w.key, 'stop', null)}>
                    Stop
                  </button>
                </>
              )}
              <button className="link" onClick={() => openInSprig(w.key)}>
                Open in sprig
              </button>
            </div>
            {w.error && <p className="error">{w.error}</p>}
            {failure?.key === w.key && (
              <p className="error">
                {failure.result.error}
                {failure.result.needsSprig && (
                  <>
                    {' '}
                    <button className="link" onClick={() => openInSprig(w.key)}>
                      Sort it out in sprig
                    </button>
                  </>
                )}
              </p>
            )}
            {w.apps.length === 0 ? (
              <p className="empty">No apps.</p>
            ) : (
              <ul className="plain sprig-apps">
                {w.apps.map((a) => (
                  <li key={a.name}>
                    <span className={`sprig-status tone-${appTone(a)}`}>
                      <span className="dot" />
                      {a.state === 'exited' && a.exit !== null ? `exited ${a.exit}` : a.state}
                    </span>
                    <span className="who" title={a.name}>
                      {a.shortName}
                    </span>
                    {a.port !== null && (
                      <button
                        className="link mono"
                        title={`Open http://localhost:${a.port} (middle click: new window)`}
                        onClick={() => request('app.openUrl', { url: `http://localhost:${a.port}`, newWindow: false }).catch((e: Error) => ctx.onError(e.message))}
                        onAuxClick={(e) => e.button === 1 && request('app.openUrl', { url: `http://localhost:${a.port}`, newWindow: true }).catch((err: Error) => ctx.onError(err.message))}
                      >
                        :{a.port}
                      </button>
                    )}
                    {(a.error ?? a.doing) && <span className={a.error ? 'error small' : 'muted small'}>{a.error ?? a.doing}</span>}
                    <span className="grow" />
                    {a.hasLog && (
                      <button className="link" onClick={() => setLog({ key: w.key, app: a.name })}>
                        Log
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        );
      })}
      <SprigLogDialog taskId={task.id} workspaceKey={log?.key ?? ''} app={log?.app ?? null} onClose={() => setLog(null)} />
    </section>
  );
}

export const sprigUi: ModuleUi = {
  id: 'sprig',
  FolderStatus: SprigFolderStatus,
  TaskSection: SprigApps,
};
