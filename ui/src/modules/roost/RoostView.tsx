import { useCallback, useEffect, useMemo, useRef, useState, type DragEvent } from 'react';
import { useAttention } from '../../attention/useAttention';
import { on, request, type RoostGridDto, type RoostRegionDto } from '../../bridge';
import { Menu } from '../../Menu';
import { useSessions } from '../../sessions/useSessions';
import { agentWord, sessionName, StatePill, statusOf, type AgentStatus } from '../../tasks/present';
import type { AppContext } from '../types';
import { Pane, regionArea, sessionDragType, startSessionDrag, type DropProps } from './Pane';

export const roostViewKey = 'roost';

interface Props {
  /** The task whose agents are shown; kept per window by the app. */
  taskId: string | null;
  setTaskId: (taskId: string | null) => void;
  ctx: AppContext;
}

/** A task's agents side by side (M5, ADR 0013). The layout and who sits where live on the host, shared by windows. */
export function RoostView({ taskId, setTaskId, ctx }: Props) {
  const task = taskId ? ctx.tasks.find((t) => t.id === taskId) : undefined;
  if (!task) return <ChooseTask ctx={ctx} onChoose={setTaskId} />;
  return <Grid key={task.id} taskId={task.id} ctx={ctx} onChooseTask={setTaskId} />;
}

function ChooseTask({ ctx, onChoose }: { ctx: AppContext; onChoose: (taskId: string) => void }) {
  const withAgents = ctx.tasks.filter((t) => t.state !== 'done' && t.sessions.length > 0);
  return (
    <main className="content">
      <div className="page">
        <h2>Grid</h2>
        <p className="muted">A task's agents side by side, each with its thread and composer. Choose a task.</p>
        {withAgents.length === 0 ? (
          <p className="empty">No open task has agents yet. Start one from a task.</p>
        ) : (
          <ul className="plain roost-tasks">
            {withAgents.map((t) => (
              <li key={t.id}>
                <button className="link" onClick={() => onChoose(t.id)}>
                  {t.title}
                </button>
                <code>{t.id}</code>
                <StatePill state={t.state} small />
                <span className="muted">
                  {t.sessions.length} agent{t.sessions.length === 1 ? '' : 's'}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </main>
  );
}

function Grid({ taskId, ctx, onChooseTask }: { taskId: string; ctx: AppContext; onChooseTask: (taskId: string) => void }) {
  const { onError, openThread, reportViewing } = ctx;
  const task = ctx.tasks.find((t) => t.id === taskId);
  const [grid, setGrid] = useState<RoostGridDto | null>(null);
  const [focused, setFocused] = useState<number | null>(null);
  const [dropOn, setDropOn] = useState<number | null>(null);
  const live = useSessions();
  const attention = useAttention();
  const stage = useRef<HTMLElement>(null);
  // A region to focus once the grid that shows it has rendered (after a zoom changes which pane is on screen).
  const pendingFocus = useRef<number | null>(null);

  const load = useCallback(() => {
    // Only shapes a grid the task doesn't have yet; the stage is roughly the window less the bars above it.
    const aspect = window.innerWidth / Math.max(1, window.innerHeight - 100);
    request('roost.get', { taskId, aspect })
      .then(setGrid)
      .catch((e: Error) => onError(e.message));
  }, [taskId, onError]);

  useEffect(() => {
    load();
    const offs = [
      on('roost.changed', (e) => {
        if (e.taskId === taskId) load();
      }),
      // A new agent fills an empty region, and one that's gone leaves its region, on the next roost.get.
      on('sessions.changed', load),
      on('tasks.changed', (e) => {
        if (e.id === taskId) load();
      }),
    ];
    return () => offs.forEach((off) => off());
  }, [taskId, load]);

  const change = useCallback(
    (p: Promise<RoostGridDto>) => {
      p.then(setGrid).catch((e: Error) => onError(e.message));
    },
    [onError],
  );
  const zoom = useCallback((regionId: number | null) => change(request('roost.zoom', { taskId, regionId })), [change, taskId]);
  const assign = useCallback((regionId: number, sessionId: string) => change(request('roost.assign', { taskId, regionId, sessionId })), [change, taskId]);
  const remove = useCallback((sessionId: string) => change(request('roost.unassign', { taskId, sessionId })), [change, taskId]);
  const openOne = useCallback((sessionId: string) => openThread(sessionId, taskId), [openThread, taskId]);

  const byId = useMemo(() => new Map(live.map((s) => [s.id, s])), [live]);
  const nameOf = (id: string) => {
    const s = byId.get(id) ?? task?.sessions.find((x) => x.id === id);
    return s ? sessionName(s) : id.slice(0, 8);
  };
  const statusFor = (id: string): AgentStatus => {
    const s = byId.get(id);
    return s ? statusOf(s) : 'ended';
  };
  const needing = useMemo(() => new Set(attention.items.filter((i) => i.needsYou).map((i) => i.sessionId)), [attention]);

  const zoomedRegion = grid?.regions.find((r) => r.id === grid.zoomed && r.sessionId);
  const visibleKey = grid ? (zoomedRegion ? [zoomedRegion] : grid.regions).flatMap((r) => (r.sessionId ? [r.sessionId] : [])).join(',') : '';
  useEffect(() => reportViewing(roostViewKey, visibleKey ? visibleKey.split(',') : []), [visibleKey, reportViewing]);

  const drop: DropProps = useMemo(
    () => ({
      onDragOver: (e: DragEvent) => {
        if (!e.dataTransfer.types.includes(sessionDragType)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        setDropOn(Number((e.currentTarget as HTMLElement).dataset.region));
      },
      onDragLeave: (e: DragEvent) => {
        // Moving onto a child of the same region isn't leaving it.
        if (e.currentTarget.contains(e.relatedTarget as Node | null)) return;
        setDropOn(null);
      },
      onDrop: (e: DragEvent) => {
        setDropOn(null);
        const sessionId = e.dataTransfer.getData(sessionDragType);
        if (!sessionId) return;
        e.preventDefault();
        assign(Number((e.currentTarget as HTMLElement).dataset.region), sessionId);
      },
    }),
    [assign],
  );

  // Puts the caret where the user can act: a pending prompt's main button, else the composer, else the pane.
  const focusRegion = useCallback((regionId: number) => {
    const el = stage.current?.querySelector<HTMLElement>(`[data-region="${regionId}"]`);
    if (!el || el.hidden) return false;
    const target = el.querySelector<HTMLElement>('.card .primary:not(:disabled)') ?? el.querySelector<HTMLElement>('.composer textarea') ?? el.querySelector<HTMLElement>('.roost-pick button') ?? el;
    target.focus();
    return true;
  }, []);

  useEffect(() => {
    if (pendingFocus.current !== null && focusRegion(pendingFocus.current)) pendingFocus.current = null;
  }, [grid, focusRegion]);

  // Shows a region and focuses it; when another pane is zoomed, the zoom moves to it (or the grid restores, for an
  // empty region).
  const goTo = useCallback(
    (r: RoostRegionDto) => {
      if (grid?.zoomed != null && grid.zoomed !== r.id) {
        pendingFocus.current = r.id;
        zoom(r.sessionId ? r.id : null);
      } else {
        focusRegion(r.id);
      }
    },
    [grid, zoom, focusRegion],
  );

  useEffect(() => {
    if (!grid) return;
    const key = (e: KeyboardEvent) => {
      if (e.target instanceof Element && e.target.closest('.xterm')) return;
      const ctrl = e.ctrlKey || e.metaKey;
      if (e.altKey && !ctrl && !e.shiftKey && /^[1-8]$/.test(e.key)) {
        const r = grid.regions.find((x) => x.number === Number(e.key));
        if (!r) return;
        e.preventDefault();
        goTo(r);
      } else if (ctrl && e.shiftKey && !e.altKey && e.key.toLowerCase() === 'z') {
        const inPane = document.activeElement?.closest<HTMLElement>('[data-region]');
        const regionId = inPane ? Number(inPane.dataset.region) : focused;
        const r = grid.regions.find((x) => x.id === regionId && x.sessionId);
        if (!r) return;
        e.preventDefault();
        pendingFocus.current = r.id;
        zoom(r.id);
      } else if (e.key === 'Escape' && !ctrl && !e.altKey && !e.shiftKey && grid.zoomed != null) {
        // An open menu or dialog takes Esc first.
        if (document.querySelector('.menu-list, dialog[open]')) return;
        e.preventDefault();
        pendingFocus.current = grid.zoomed;
        zoom(null);
      } else if (ctrl && !e.altKey && !e.shiftKey && e.key === '.') {
        // The grid takes Ctrl+. only while one of its panes needs the user; otherwise the app walks the inbox.
        const needs = grid.regions.filter((r) => r.sessionId && needing.has(r.sessionId));
        if (needs.length === 0) return;
        e.preventDefault();
        e.stopPropagation();
        const at = needs.findIndex((r) => r.id === focused);
        goTo(needs[(at + 1) % needs.length]);
      }
    };
    // Capture, so Ctrl+. is decided here before the app's own handler sees it.
    window.addEventListener('keydown', key, true);
    return () => window.removeEventListener('keydown', key, true);
  }, [grid, focused, needing, goTo, zoom]);

  if (!grid) return <p className="empty roost-loading">Loading...</p>;

  const placed = grid.regions.filter((r) => r.sessionId);
  const presetName = grid.presets.find((p) => p.key === grid.preset)?.name ?? 'Custom';
  const keyCount = Math.min(8, grid.regions.length);
  const otherTasks = ctx.tasks.filter((t) => t.state !== 'done' && t.sessions.length > 0);

  return (
    <div className="roost-view">
      <div className="roost-toolbar">
        <button className="link roost-task" title="Open the task" onClick={() => ctx.openTask(taskId)}>
          {task?.title ?? taskId}
        </button>
        <code className="muted">{taskId}</code>
        {task && <StatePill state={task.state} small />}
        {otherTasks.length > 1 && (
          <Menu
            ariaLabel="Show another task's agents"
            buttonClass="link roost-switch"
            label={<span className="chevron" aria-hidden="true" />}
            items={otherTasks.map((t) => ({
              key: t.id,
              label: (
                <span className="grow">
                  {t.title} <code className="muted">{t.id}</code>
                </span>
              ),
              current: t.id === taskId,
              onSelect: () => onChooseTask(t.id),
            }))}
          />
        )}
        {zoomedRegion ? (
          <>
            <span className="roost-zoomtag">
              Zoomed: {zoomedRegion.number} of {grid.regions.length}
            </span>
            <button onClick={() => zoom(null)}>Restore</button>
            <span className="roost-keys">
              <kbd>Esc</kbd> restores
            </span>
            <span className="spacer" />
            {placed.length > 1 && <span className="roost-label">Also in the grid</span>}
            {placed
              .filter((r) => r !== zoomedRegion)
              .map((r) => {
                const status = statusFor(r.sessionId!);
                return (
                  <button key={r.id} className={`roost-mini status-${status}`} title={`Zoom ${nameOf(r.sessionId!)} instead`} onClick={() => goTo(r)}>
                    <span className="dot" />
                    {nameOf(r.sessionId!)}
                  </button>
                );
              })}
          </>
        ) : (
          <>
            <span className="spacer" />
            {grid.unplaced.length > 0 && <span className="roost-label">Not in the grid</span>}
            {grid.unplaced.map((sessionId) => {
              const status = statusFor(sessionId);
              const name = nameOf(sessionId);
              return (
                <div key={sessionId} className="roost-chip-wrap" draggable onDragStart={(e) => startSessionDrag(e, sessionId)}>
                  <Menu
                    ariaLabel={`Put ${name} in the grid`}
                    buttonClass={`agent-chip roost-chip status-${status}`}
                    label={
                      <>
                        <span className="dot" />
                        {name}
                        <span className="st">{agentWord(status)}</span>
                      </>
                    }
                    items={grid.regions.map((r) => ({
                      key: String(r.id),
                      label: (
                        <span className="grow">
                          Into {r.number}
                          <span className="muted">{r.sessionId ? `, in place of ${nameOf(r.sessionId)}` : ', empty'}</span>
                        </span>
                      ),
                      onSelect: () => assign(r.id, sessionId),
                    }))}
                  />
                </div>
              );
            })}
            <div className="roost-layout">
              <Menu
                ariaLabel={`Layout: ${presetName}`}
                label={
                  <>
                    Layout: {presetName}
                    <span className="chevron" aria-hidden="true" />
                  </>
                }
                items={grid.presets.map((p) => ({
                  key: p.key,
                  label: (
                    <>
                      <span className="roost-thumb" style={{ gridTemplateColumns: `repeat(${grid.units}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${grid.units}, minmax(0, 1fr))` }}>
                        {p.regions.map((r) => (
                          <span key={r.id} style={regionArea(r)} />
                        ))}
                      </span>
                      {p.name}
                    </>
                  ),
                  current: p.key === grid.preset,
                  onSelect: () => change(request('roost.applyPreset', { taskId, preset: p.key })),
                }))}
              />
            </div>
            <span className="roost-keys">
              <kbd>Alt</kbd> <kbd>1</kbd>–<kbd>{keyCount}</kbd> pane · <kbd>Ctrl</kbd> <kbd>Shift</kbd> <kbd>Z</kbd> zoom · <kbd>Ctrl</kbd> <kbd>.</kbd> next needing you
            </span>
          </>
        )}
      </div>

      <main
        className="roost-stage"
        ref={stage}
        aria-label={`Agents of ${taskId}`}
        style={{ gridTemplateColumns: `repeat(${grid.units}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${grid.units}, minmax(0, 1fr))` }}
      >
        {grid.regions.map((r) =>
          r.sessionId ? (
            <Pane
              key={r.sessionId}
              region={r}
              sessionId={r.sessionId}
              focused={focused === r.id}
              needsYou={needing.has(r.sessionId)}
              zoomed={zoomedRegion === r}
              hidden={zoomedRegion !== undefined && zoomedRegion !== r}
              dropping={dropOn === r.id}
              drop={drop}
              onFocus={setFocused}
              onZoom={zoom}
              onRemove={remove}
              onOpenThread={openOne}
              onError={onError}
            />
          ) : (
            !zoomedRegion && (
              <section
                key={`empty-${r.id}`}
                className={dropOn === r.id ? 'roost-pane roost-empty drop' : 'roost-pane roost-empty'}
                style={regionArea(r)}
                data-region={r.id}
                tabIndex={-1}
                aria-label={`${r.number}: empty`}
                onFocus={() => setFocused(r.id)}
                {...drop}
              >
                <span className="roost-num">{r.number}</span>
                <strong>Empty</strong>
                {grid.unplaced.length + placed.length === 0 ? (
                  <p className="note">
                    No agents yet.{' '}
                    <button className="link" onClick={() => ctx.openTask(taskId)}>
                      Start one from the task.
                    </button>
                  </p>
                ) : (
                  <>
                    <div className="roost-pick">
                      {grid.unplaced.map((sessionId) => (
                        <button key={sessionId} className={`status-${statusFor(sessionId)}`} onClick={() => assign(r.id, sessionId)}>
                          <span className="dot" />
                          {nameOf(sessionId)}
                          <span className="st">{agentWord(statusFor(sessionId))}</span>
                        </button>
                      ))}
                      {placed.map((p) => (
                        <button key={p.sessionId} className={`status-${statusFor(p.sessionId!)}`} onClick={() => assign(r.id, p.sessionId!)}>
                          <span className="dot" />
                          {nameOf(p.sessionId!)}
                          <span className="st">in {p.number} · moves here</span>
                        </button>
                      ))}
                    </div>
                    <p className="note">Choose an agent, or drag one's header here.</p>
                  </>
                )}
              </section>
            )
          ),
        )}
      </main>
    </div>
  );
}
