import { memo, type CSSProperties, type DragEvent } from 'react';
import { useReportSeen } from '../../attention/useAttention';
import type { RoostRegionDto } from '../../bridge';
import { Menu } from '../../Menu';
import { Composer, modelLabel, ThreadItems, useSessionThread } from '../../sessions/SessionView';
import { agentWord, sessionName, statusOf } from '../../tasks/present';

/** The drag payload: a session id, under a type of Ochre's own so text dropped from elsewhere is ignored. */
export const sessionDragType = 'application/x-ochre-session';

export function startSessionDrag(e: DragEvent, sessionId: string) {
  e.dataTransfer.setData(sessionDragType, sessionId);
  e.dataTransfer.effectAllowed = 'move';
}

/** Where a region sits on the grid's units (rows and columns count from 0). */
export const regionArea = (r: RoostRegionDto): CSSProperties => ({ gridRow: `${r.row + 1} / span ${r.rowSpan}`, gridColumn: `${r.column + 1} / span ${r.columnSpan}` });

const zoomedArea: CSSProperties = { gridRow: '1 / -1', gridColumn: '1 / -1' };

/** Drop handlers shared by every region; each reads its region from the element's data-region. */
export interface DropProps {
  onDragOver: (e: DragEvent) => void;
  onDragLeave: (e: DragEvent) => void;
  onDrop: (e: DragEvent) => void;
}

interface Props {
  region: RoostRegionDto;
  sessionId: string;
  focused: boolean;
  needsYou: boolean;
  zoomed: boolean;
  /** Another pane is zoomed. Kept mounted, so its scroll position and an unsent draft survive the zoom. */
  hidden: boolean;
  dropping: boolean;
  drop: DropProps;
  onFocus: (regionId: number) => void;
  onZoom: (regionId: number) => void;
  onRemove: (sessionId: string) => void;
  onOpenThread: (sessionId: string) => void;
  onError: (message: string) => void;
}

/**
 * One agent in the grid: a header to drag, zoom and act on it, and its live thread with the composer and prompts.
 * Memoised so a change elsewhere in the grid (focus, another pane's status) doesn't re-render this thread.
 */
export const Pane = memo(function Pane({ region, sessionId, focused, needsYou, zoomed, hidden, dropping, drop, onFocus, onZoom, onRemove, onOpenThread, onError }: Props) {
  const { header, items, turnActive, gone } = useSessionThread(sessionId, onError);
  useReportSeen(hidden ? null : sessionId);

  const name = header ? sessionName(header) : sessionId.slice(0, 8);
  const status = header ? statusOf(header) : 'ended';
  const observed = header?.observed ?? false;
  const meta = !header ? '' : observed ? (header.branch ?? '') : [modelLabel(header.model), header.contextTokens > 0 ? `${Math.round(header.contextTokens / 1000)}k` : ''].filter(Boolean).join(' · ');
  const classes = ['roost-pane', needsYou ? 'need' : '', focused ? 'focus' : '', dropping ? 'drop' : '', zoomed ? 'zoomed' : ''].filter(Boolean).join(' ');

  return (
    <section
      className={classes}
      style={zoomed ? zoomedArea : regionArea(region)}
      data-region={region.id}
      hidden={hidden}
      tabIndex={-1}
      aria-label={`${region.number}: ${name}, ${agentWord(status)}`}
      onFocus={() => onFocus(region.id)}
      {...drop}
    >
      <div className="roost-head" draggable title="Drag onto another pane to swap them" onDragStart={(e) => startSessionDrag(e, sessionId)}>
        <span className="roost-num">{region.number}</span>
        <span className={`status status-${status}`}>
          <span className="dot" />
          {agentWord(status)}
        </span>
        <span className={observed ? 'roost-name mono' : 'roost-name'} title={header?.cwd}>
          {name}
        </span>
        {observed && <span className="tag-outside">outside Ochre</span>}
        <span className="spacer" />
        {meta && <span className="roost-meta">{meta}</span>}
        <button className="link icon-only" aria-label={zoomed ? `Restore the grid` : `Zoom ${name}`} title={zoomed ? 'Restore (Esc)' : 'Zoom (Ctrl+Shift+Z)'} onClick={() => onZoom(region.id)}>
          {zoomed ? (
            <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">
              <path d="M5 2v3H2M12 5H9V2M9 12V9h3M2 9h3v3" fill="none" stroke="currentColor" strokeWidth="1.4" />
            </svg>
          ) : (
            <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true">
              <path d="M2 5V2h3M9 2h3v3M12 9v3H9M5 12H2V9" fill="none" stroke="currentColor" strokeWidth="1.4" />
            </svg>
          )}
        </button>
        <Menu
          ariaLabel={`More for ${name}`}
          buttonClass="link more"
          label={<span aria-hidden="true">⋯</span>}
          items={[
            { key: 'open', label: 'Open thread', onSelect: () => onOpenThread(sessionId) },
            { key: 'remove', label: 'Remove from grid', onSelect: () => onRemove(sessionId) },
          ]}
        />
      </div>
      {gone ? (
        <p className="empty roost-gone">Ochre doesn't know this agent. It may belong to a task that was deleted.</p>
      ) : !header ? (
        <p className="empty roost-gone">Loading...</p>
      ) : (
        <>
          <ThreadItems sessionId={sessionId} items={items} turnActive={turnActive} compact hidden={hidden} emptyText="Nothing yet." onError={onError} />
          {observed ? (
            <p className="read-only-note muted">Read-only: started outside Ochre. Carry on with it where it's running.</p>
          ) : (
            <Composer sessionId={sessionId} queued={header.queuedPrompts} dormant={header.status === 'dormant'} autoFocus={false} compact={{ name, busy: turnActive }} onError={onError} />
          )}
        </>
      )}
    </section>
  );
});
