import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
  on,
  request,
  type GraphChangeDto,
  type GraphCommitDto,
  type GraphPageDto,
  type GraphRefDto,
  type GraphRowDto,
  type GraphScopeDto,
  type GraphWorktreeDto,
  type TaskListItem,
} from '../../bridge';
import { Menu } from '../../Menu';
import { when } from '../../tasks/present';
import type { AppContext } from '../types';

/** What the Git view shows: a task's folder, how much of its history, and (for one branch) the base to measure against. */
export interface GraphTarget {
  task: string;
  folder: string;
  scope: GraphScopeDto;
  /** For the branch scope: a base branch, "" for none, null to let Ochre pick. */
  base?: string | null;
}

export const graphParam = (t: GraphTarget) => JSON.stringify(t);

function parseParam(param: string | null): GraphTarget | null {
  if (!param) return null;
  try {
    const v = JSON.parse(param) as Partial<GraphTarget>;
    if (typeof v.task === 'string' && typeof v.folder === 'string' && (v.scope === 'all' || v.scope === 'worktrees' || v.scope === 'branch'))
      return { task: v.task, folder: v.folder, scope: v.scope, base: typeof v.base === 'string' ? v.base : null };
  } catch {
    // A stale or hand-made parameter: fall back to the default.
  }
  return null;
}

const ROW = 28;
const LANE = 14;
const PAD = 10;
// Perch's dark lane set: distinct from one another and from the status greens and reds.
const LANE_COLORS = ['#6aa0e8', '#a391e6', '#45c2b1', '#de7daf', '#a2cc5c', '#e3b44e', '#52b2df', '#e39858'];
const laneColor = (i: number) => LANE_COLORS[i % LANE_COLORS.length];
const laneX = (lane: number) => PAD + lane * LANE;

const leaf = (path: string) => path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? path;
const samePath = (a: string, b: string) => a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase();
const short = (hash: string) => hash.slice(0, 7);
const ago = (unix: number) => when(new Date(unix * 1000).toISOString());
const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

interface Folder {
  task: TaskListItem;
  path: string;
  worktree: boolean;
}

function taskFolders(tasks: TaskListItem[]): Folder[] {
  const open = [...tasks.filter((t) => t.state !== 'done'), ...tasks.filter((t) => t.state === 'done')];
  return open.flatMap((t) => [...t.repos.map((path) => ({ task: t, path, worktree: false })), ...t.worktrees.map((w) => ({ task: t, path: w.path, worktree: true }))]);
}

/** The git module's title-bar view: a task folder's commit graph, read-only. */
export function GraphView({ param, setParam, ctx }: { param: string | null; setParam: (p: string | null) => void; ctx: AppContext }) {
  const folders = useMemo(() => taskFolders(ctx.tasks), [ctx.tasks]);
  const target: GraphTarget | null = useMemo(() => {
    const parsed = parseParam(param);
    if (parsed) return parsed;
    const first = folders[0];
    return first ? { task: first.task.id, folder: first.path, scope: 'all' } : null;
  }, [param, folders]);

  const [page, setPage] = useState<GraphPageDto | null>(null);
  const [rows, setRows] = useState<GraphRowDto[]>([]);
  const [more, setMore] = useState<{ width: number; hasMore: boolean; openEdges: number[] }>({ width: 1, hasMore: false, openEdges: [] });
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [find, setFind] = useState('');
  const [reloads, setReloads] = useState(0);
  const lastLoad = useRef(0);
  const { onError } = ctx;

  const go = useCallback((t: GraphTarget) => setParam(graphParam(t)), [setParam]);
  const reload = useCallback(() => setReloads((n) => n + 1), []);

  const targetKey = target ? graphParam(target) : '';
  useEffect(() => {
    if (!target) return;
    let live = true;
    setLoading(true);
    request('graph.load', { taskId: target.task, folder: target.folder, scope: target.scope, base: target.scope === 'branch' ? (target.base ?? null) : null })
      .then((p) => {
        if (!live) return;
        lastLoad.current = Date.now();
        setPage(p);
        setRows(p.rows);
        setMore({ width: p.width, hasMore: p.hasMore, openEdges: p.openEdges });
        // Keep the selection across a refresh when its commit is still there; otherwise land on the folder's own work.
        setSelected((s) => (s && p.rows.some((r) => r.hash === s) ? s : homeRow(p)));
      })
      .catch((e: Error) => live && onError(e.message))
      .finally(() => live && setLoading(false));
    return () => {
      live = false;
    };
    // targetKey stands for target: a fresh object each render with the same contents mustn't reload.
  }, [targetKey, reloads, onError]);

  // History follows what the user and agents do, never a timer: coming back to the window, or an agent's turn ending.
  useEffect(() => {
    const focus = () => {
      if (Date.now() - lastLoad.current > 5000) reload();
    };
    window.addEventListener('focus', focus);
    const off = on('git.changed', (e) => {
      if (e.taskId === target?.task) reload();
    });
    return () => {
      window.removeEventListener('focus', focus);
      off();
    };
  }, [reload, target?.task]);

  const loadMore = () => {
    if (!page || loadingMore) return;
    setLoadingMore(true);
    request('graph.more', { token: page.token })
      .then((m) => {
        setRows((r) => [...r, ...m.rows]);
        setMore({ width: m.width, hasMore: m.hasMore, openEdges: m.openEdges });
      })
      .catch((e: Error) => (e.message.includes('expired') ? reload() : onError(e.message)))
      .finally(() => setLoadingMore(false));
  };

  const refsAt = useMemo(() => groupBy(page?.refs ?? [], (r) => r.target), [page]);
  const worktreesAt = useMemo(() => groupBy((page?.worktrees ?? []).filter((w) => w.head), (w) => w.head!), [page]);
  const rowOf = useMemo(() => new Map(rows.map((r, i) => [r.hash, i])), [rows]);

  const matches = useMemo(() => {
    const q = find.trim().toLowerCase();
    if (!q) return [];
    const hex = /^[0-9a-f]{4,}$/.test(q);
    const out: number[] = [];
    rows.forEach((r, i) => {
      if (r.kind === 'wip') return;
      if (
        r.subject.toLowerCase().includes(q) ||
        r.author.toLowerCase().includes(q) ||
        (hex && r.kind === 'commit' && r.hash.startsWith(q)) ||
        (refsAt.get(r.hash) ?? []).some((ref) => ref.name.toLowerCase().includes(q))
      )
        out.push(i);
    });
    return out;
  }, [find, rows, refsAt]);
  const matchSet = useMemo(() => new Set(matches), [matches]);

  const list = useRef<HTMLDivElement>(null);
  const [view, setView] = useState({ top: 0, height: 600 });
  useEffect(() => {
    const el = list.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setView({ top: el.scrollTop, height: el.clientHeight }));
    ro.observe(el);
    return () => ro.disconnect();
  }, [page]);

  const reveal = useCallback((index: number) => {
    const el = list.current;
    if (!el || index < 0) return;
    const top = index * ROW;
    if (top < el.scrollTop || top + ROW > el.scrollTop + el.clientHeight) el.scrollTop = Math.max(0, top - el.clientHeight / 2);
  }, []);

  const select = useCallback(
    (hash: string) => {
      setSelected(hash);
      reveal(rowOf.get(hash) ?? -1);
    },
    [reveal, rowOf],
  );

  const step = (dir: 1 | -1) => {
    if (matches.length === 0) return;
    const at = rowOf.get(selected ?? '') ?? -1;
    const next = dir === 1 ? (matches.find((m) => m > at) ?? matches[0]) : ([...matches].reverse().find((m) => m < at) ?? matches[matches.length - 1]);
    select(rows[next].hash);
  };

  const onListKey = (e: KeyboardEvent) => {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    e.preventDefault();
    const at = rowOf.get(selected ?? '') ?? -1;
    const next = Math.min(rows.length - 1, Math.max(0, at + (e.key === 'ArrowDown' ? 1 : -1)));
    if (rows[next]) select(rows[next].hash);
  };

  if (!target) {
    return (
      <main className="content">
        <p className="empty">Add a repo to a task to see its history here.</p>
      </main>
    );
  }

  const task = ctx.tasks.find((t) => t.id === target.task);
  const current = page?.worktrees.find((w) => w.current);
  const graphWidth = Math.min(260, Math.max(40, PAD * 2 + (Math.max(1, more.width) - 1) * LANE));
  const first = Math.max(0, Math.floor(view.top / ROW) - 10);
  const last = Math.min(rows.length, Math.ceil((view.top + view.height) / ROW) + 10);
  const selectedRow = selected ? rows[rowOf.get(selected) ?? -1] : undefined;
  const at = matches.indexOf(rowOf.get(selected ?? '') ?? -1);
  const commits = rows.filter((r) => r.kind === 'commit').length;

  return (
    <div className="graph-view" style={{ ['--graph-w' as string]: `${graphWidth}px` }}>
      <div className="graph-toolbar">
        <Menu
          buttonClass="graph-picker"
          ariaLabel="Repo"
          label={
            <>
              <strong>{leaf(page?.root ?? target.folder)}</strong>
              <span className="path muted" title={target.folder}>
                <span dir="ltr">{target.folder}</span>
              </span>
              <span className="chevron" aria-hidden="true" />
            </>
          }
          items={folders.map((f) => ({
            key: `${f.task.id} ${f.path}`,
            label: (
              <span className="graph-folder-item">
                <span>
                  {f.task.id} <span className="muted">{f.task.title}</span>
                </span>
                <span className="muted">
                  {f.worktree ? 'worktree ' : ''}
                  {f.path}
                </span>
              </span>
            ),
            current: f.task.id === target.task && samePath(f.path, target.folder),
            onSelect: () => go({ task: f.task.id, folder: f.path, scope: f.worktree ? 'branch' : 'all' }),
          }))}
        />
        <div className="seg" role="group" aria-label="Show">
          {(
            [
              ['all', 'All'],
              ['worktrees', 'Worktrees'],
              ['branch', 'This branch'],
            ] as const
          ).map(([scope, label]) => (
            <button key={scope} aria-pressed={target.scope === scope} onClick={() => go({ ...target, scope })}>
              {label}
            </button>
          ))}
        </div>
        {target.scope === 'branch' && page && !page.error && (
          <>
            <span className="muted">against</span>
            <Menu
              buttonClass="graph-base"
              ariaLabel="Base branch"
              label={
                <>
                  {page.base ?? 'nothing'}
                  <span className="chevron" aria-hidden="true" />
                </>
              }
              items={[
                ...page.baseCandidates.map((b) => ({ key: b, label: b, current: b === page.base, onSelect: () => go({ ...target, base: b }) })),
                { key: '', label: 'Nothing: its whole history', current: page.base === null, separatorBefore: page.baseCandidates.length > 0, onSelect: () => go({ ...target, base: '' }) },
              ]}
            />
          </>
        )}
        <span className="spacer" />
        <label className="graph-find">
          <span className="muted">Find</span>
          <input
            type="search"
            placeholder="Message, author, branch or hash"
            value={find}
            onChange={(e) => setFind(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') step(e.shiftKey ? -1 : 1);
              if (e.key === 'Escape') setFind('');
            }}
          />
        </label>
        {find.trim() && <span className="muted graph-count">{matches.length === 0 ? 'no matches' : `${at >= 0 ? at + 1 : 0} of ${matches.length}`}</span>}
        <button className="link icon-only" aria-label="Refresh" title="Refresh" onClick={reload} disabled={loading}>
          <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M13 8a5 5 0 1 1-1.5-3.6M13 2.5v3h-3" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </button>
      </div>

      {target.scope === 'branch' && page && !page.error && (
        <div className="graph-context">
          {task && (
            <span>
              Task{' '}
              <button className="link" onClick={() => ctx.openTask(task.id)}>
                {task.id}
              </button>{' '}
              <span className="muted">{task.title}</span>
            </span>
          )}
          {page.base && page.ahead !== null && (
            <span className="graph-pill">
              <span className="git-ahead">↑{page.ahead}</span> ahead of {page.base}
            </span>
          )}
          {page.base && page.behind !== null && (
            <span className="graph-pill">
              <span className="git-behind">↓{page.behind}</span> behind
            </span>
          )}
          {current && current.changes > 0 && <span className="graph-pill">{current.changes} uncommitted</span>}
        </div>
      )}

      {page?.error ? (
        <main className="content">
          <p className="empty">{page.error}</p>
        </main>
      ) : (
        <div className="graph-body">
          <Rail page={page} rowOf={rowOf} onSelect={select} />
          <main className="graph-main">
            <div className="graph-row graph-head" aria-hidden="true">
              <span />
              <span>Message</span>
              <span>Author</span>
              <span>Date</span>
              <span>Commit</span>
            </div>
            <div className="graph-list" ref={list} tabIndex={0} onKeyDown={onListKey} onScroll={(e) => setView({ top: e.currentTarget.scrollTop, height: e.currentTarget.clientHeight })}>
              {!page && loading && <p className="empty">Reading history...</p>}
              {page && rows.length === 0 && <p className="empty">No commits yet.</p>}
              <div style={{ height: rows.length * ROW, position: 'relative' }}>
                {rows.slice(first, last).map((r, k) => {
                  const i = first + k;
                  return (
                    <Row
                      key={r.hash}
                      row={r}
                      index={i}
                      last={i === rows.length - 1}
                      openEdges={more.openEdges}
                      refs={refsAt.get(r.hash) ?? []}
                      worktrees={worktreesAt.get(r.hash) ?? []}
                      page={page!}
                      current={current}
                      selected={r.hash === selected}
                      match={matchSet.has(i)}
                      graphWidth={graphWidth}
                      onSelect={() => setSelected(r.hash)}
                    />
                  );
                })}
              </div>
              {page && rows.length > 0 && (
                <div className="graph-more">
                  <span className="muted">{plural(commits, 'commit')} loaded</span>
                  {more.hasMore && (
                    <button onClick={loadMore} disabled={loadingMore}>
                      {loadingMore ? 'Loading...' : 'Load 2,000 more'}
                    </button>
                  )}
                </div>
              )}
            </div>
          </main>
          <aside className="graph-detail" aria-label="Details">
            {page && selectedRow ? (
              <Detail key={`${page.token} ${selectedRow.hash}`} page={page} row={selectedRow} rows={rows} rowOf={rowOf} refs={refsAt.get(selectedRow.hash) ?? []} worktrees={worktreesAt.get(selectedRow.hash) ?? []} onSelect={select} onError={onError} onExpired={reload} />
            ) : (
              <p className="empty">Select a commit to see its details.</p>
            )}
          </aside>
        </div>
      )}
    </div>
  );
}

function homeRow(p: GraphPageDto): string | null {
  const here = p.worktrees.find((w) => w.current);
  if (here) {
    const wip = p.rows.find((r) => r.kind === 'wip' && samePath(r.hash.slice(4), here.path));
    if (wip) return wip.hash;
    if (here.head && p.rows.some((r) => r.hash === here.head)) return here.head;
  }
  return p.rows[0]?.hash ?? null;
}

function groupBy<T>(items: T[], key: (t: T) => string): Map<string, T[]> {
  const m = new Map<string, T[]>();
  for (const it of items) {
    const k = key(it);
    const list = m.get(k);
    if (list) list.push(it);
    else m.set(k, [it]);
  }
  return m;
}

interface RowProps {
  row: GraphRowDto;
  index: number;
  last: boolean;
  openEdges: number[];
  refs: GraphRefDto[];
  worktrees: GraphWorktreeDto[];
  page: GraphPageDto;
  current: GraphWorktreeDto | undefined;
  selected: boolean;
  match: boolean;
  graphWidth: number;
  onSelect: () => void;
}

function Row({ row, index, last, openEdges, refs, worktrees, page, current, selected, match, graphWidth, onSelect }: RowProps) {
  const x = laneX(row.lane);
  const color = laneColor(row.lane);
  const edges: ReactNode[] = [];
  for (let k = 0; k + 3 < row.up.length; k += 4) {
    const [from, to, c, dashed] = [row.up[k], row.up[k + 1], row.up[k + 2], row.up[k + 3]];
    const x1 = laneX(from);
    const x2 = laneX(to);
    const d = x1 === x2 ? `M${x1} ${-ROW / 2} L${x2} ${ROW / 2}` : `M${x1} ${-ROW / 2} C${x1} 0 ${x2} 0 ${x2} ${ROW / 2}`;
    edges.push(<path key={k} d={d} stroke={laneColor(c)} strokeDasharray={dashed ? '3 3' : undefined} />);
  }
  const head = row.kind === 'commit' && current?.head === row.hash;

  let subject: ReactNode;
  if (row.kind === 'wip') {
    const wt = page.worktrees.find((w) => samePath(w.path, row.hash.slice(4)));
    subject = (
      <>
        <span className="graph-text graph-pseudo">{plural(wt?.changes ?? 0, 'uncommitted change')}</span>
        {wt && <span className="graph-ref ref-worktree">{wt.name}</span>}
      </>
    );
  } else if (row.kind === 'stash') {
    const st = page.stashes.find((s) => s.rowHash === row.hash);
    subject = (
      <>
        <span className="graph-ref ref-stash">{st?.selector ?? 'stash'}</span>
        <span className="graph-text graph-pseudo">{st?.text ?? row.subject}</span>
      </>
    );
  } else {
    subject = (
      <>
        {refs.map((r) => (
          <RefPill key={`${r.kind} ${r.name}`} r={r} lane={row.lane} />
        ))}
        {worktrees.map((w) => (
          <span key={w.path} className="graph-ref ref-worktree" title={w.path}>
            {w.current ? 'here' : w.name}
          </span>
        ))}
        <span className="graph-text">{row.subject}</span>
      </>
    );
  }

  return (
    <div
      className={['graph-row', selected ? 'selected' : '', match ? 'match' : ''].join(' ')}
      style={{ position: 'absolute', top: index * ROW, left: 0, right: 0 }}
      aria-selected={selected}
      onClick={onSelect}
    >
      <svg className="graph-lanes" width={graphWidth} height={ROW} aria-hidden="true">
        <g fill="none" strokeWidth="2" strokeLinecap="round" transform={`translate(0 ${ROW / 2})`}>
          {edges}
          {last && openEdges.map((l) => <path key={`open${l}`} d={`M${laneX(l)} 0 L${laneX(l)} ${ROW / 2 + 4}`} stroke={laneColor(l)} strokeDasharray="2 3" />)}
        </g>
        <g transform={`translate(0 ${ROW / 2})`}>
          {row.kind === 'wip' ? (
            <circle cx={x} cy={0} r={4.5} fill="var(--bg)" stroke={color} strokeWidth={1.6} strokeDasharray="2 2" />
          ) : row.kind === 'stash' ? (
            <rect x={x - 4} y={-4} width={8} height={8} rx={1.5} fill="var(--bg)" stroke={color} strokeWidth={1.6} />
          ) : (
            <>
              {head && <circle cx={x} cy={0} r={7.5} fill="none" stroke={color} strokeWidth={1.4} />}
              <circle cx={x} cy={0} r={row.merge ? 5.5 : 4.5} fill={color} stroke={row.merge ? 'var(--bg)' : undefined} strokeWidth={row.merge ? 2 : undefined} />
            </>
          )}
        </g>
      </svg>
      <span className="graph-subject">{subject}</span>
      <span className="muted graph-cell">{row.author}</span>
      <span className="muted graph-cell">{row.kind === 'wip' ? 'now' : ago(row.time)}</span>
      <span className="muted mono graph-cell">{row.kind === 'commit' ? short(row.hash) : ''}</span>
    </div>
  );
}

function RefPill({ r, lane }: { r: GraphRefDto; lane?: number }) {
  if (r.kind === 'tag') return <span className="graph-ref ref-tag">{r.name}</span>;
  if (r.kind === 'remote') return <span className="graph-ref ref-remote">{r.name}</span>;
  if (r.kind === 'stash') return <span className="graph-ref ref-stash">{r.name}</span>;
  return (
    <span className="graph-ref ref-local" style={lane !== undefined ? { borderColor: laneColor(lane) } : undefined}>
      {r.name}
    </span>
  );
}

function Rail({ page, rowOf, onSelect }: { page: GraphPageDto | null; rowOf: Map<string, number>; onSelect: (hash: string) => void }) {
  if (!page) return <aside className="graph-rail" />;
  const pick = (hash: string | null | undefined) => (hash && rowOf.has(hash) ? () => onSelect(hash) : undefined);
  const locals = page.refs.filter((r) => r.kind === 'local');
  const remotes = page.refs.filter((r) => r.kind === 'remote');
  const tags = page.refs.filter((r) => r.kind === 'tag');
  const Item = ({ onClick, children, title }: { onClick?: () => void; children: ReactNode; title?: string }) => (
    <button className="graph-rail-item" onClick={onClick} disabled={!onClick} title={onClick ? title : 'Not in the history loaded so far'}>
      {children}
    </button>
  );
  return (
    <aside className="graph-rail" aria-label="Refs">
      <RailSection title="Worktrees" count={page.worktrees.length}>
        {page.worktrees.map((w) => {
          const wip = page.rows.find((r) => r.kind === 'wip' && samePath(r.hash.slice(4), w.path));
          return (
            <Item key={w.path} onClick={pick(wip?.hash ?? w.head)} title={w.path}>
              <span className="graph-rail-name">{w.name}</span>
              <span className="muted">
                {[w.current ? 'here' : w.branch, w.changes > 0 ? `${w.changes} changed` : ''].filter(Boolean).join(' · ')}
              </span>
            </Item>
          );
        })}
      </RailSection>
      <RailSection title="Branches" count={locals.length}>
        {locals.map((r) => (
          <Item key={r.name} onClick={pick(r.target)}>
            <span className="graph-rail-name">{r.name}</span>
            <span className="muted">
              {r.upstreamGone ? 'upstream gone' : [r.ahead > 0 ? `↑${r.ahead}` : '', r.behind > 0 ? `↓${r.behind}` : ''].filter(Boolean).join(' ')}
            </span>
          </Item>
        ))}
      </RailSection>
      <RailSection title="Remote" count={remotes.length}>
        {remotes.map((r) => (
          <Item key={r.name} onClick={pick(r.target)}>
            <span className="graph-rail-name">{r.name}</span>
          </Item>
        ))}
      </RailSection>
      <RailSection title="Tags" count={tags.length}>
        {tags.map((r) => (
          <Item key={r.name} onClick={pick(r.target)}>
            <span className="graph-rail-name">{r.name}</span>
          </Item>
        ))}
      </RailSection>
      <RailSection title="Stashes" count={page.stashes.length}>
        {page.stashes.map((s) => (
          <Item key={s.selector} onClick={pick(s.rowHash)}>
            <span className="graph-rail-name">{s.text}</span>
            <span className="muted">{s.selector}</span>
          </Item>
        ))}
      </RailSection>
    </aside>
  );
}

function RailSection({ title, count, children }: { title: string; count: number; children: ReactNode }) {
  if (count === 0) return null;
  return (
    <section>
      <h4>
        <span>{title}</span>
        <span>{count}</span>
      </h4>
      {children}
    </section>
  );
}

interface DetailProps {
  page: GraphPageDto;
  row: GraphRowDto;
  rows: GraphRowDto[];
  rowOf: Map<string, number>;
  refs: GraphRefDto[];
  worktrees: GraphWorktreeDto[];
  onSelect: (hash: string) => void;
  onError: (message: string) => void;
  onExpired: () => void;
}

function Detail(props: DetailProps) {
  if (props.row.kind === 'wip') return <WipDetail {...props} />;
  if (props.row.kind === 'stash') return <StashDetail {...props} />;
  return <CommitDetail {...props} />;
}

function CommitLink({ hash, rows, rowOf, onSelect, inline }: { hash: string; rows: GraphRowDto[]; rowOf: Map<string, number>; onSelect: (hash: string) => void; inline?: boolean }) {
  const i = rowOf.get(hash);
  if (inline)
    return i !== undefined ? (
      <button className="link mono" title={rows[i].subject} onClick={() => onSelect(hash)}>
        {short(hash)}
      </button>
    ) : (
      <span className="mono">{short(hash)}</span>
    );
  return (
    <span className="graph-parent">
      {i !== undefined ? (
        <button className="link mono" onClick={() => onSelect(hash)}>
          {short(hash)}
        </button>
      ) : (
        <span className="mono muted" title="Not in the history loaded so far">
          {short(hash)}
        </span>
      )}
      {i !== undefined && <span className="muted">{rows[i].subject}</span>}
    </span>
  );
}

function CommitDetail({ page, row, rows, rowOf, refs, worktrees, onSelect, onError, onExpired }: DetailProps) {
  const [commit, setCommit] = useState<GraphCommitDto | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    request('graph.commit', { token: page.token, hash: row.hash })
      .then(setCommit)
      .catch((e: Error) => (e.message.includes('expired') ? onExpired() : onError(e.message)));
  }, [page.token, row.hash, onError, onExpired]);

  const body = commit ? reflow(commit.body.split('\n').slice(1).join('\n').trim()) : '';
  return (
    <>
      <h2>{row.subject}</h2>
      {(refs.length > 0 || worktrees.length > 0) && (
        <div className="graph-detail-refs">
          {refs.map((r) => (
            <RefPill key={`${r.kind} ${r.name}`} r={r} lane={row.lane} />
          ))}
          {worktrees.map((w) => (
            <span key={w.path} className="graph-ref ref-worktree" title={w.path}>
              checked out in {w.name}
            </span>
          ))}
        </div>
      )}
      <dl className="graph-kv">
        <dt>Commit</dt>
        <dd>
          <span className="mono graph-hash">{row.hash}</span>{' '}
          <button
            className="graph-copy"
            onClick={() =>
              copyText(row.hash).then((ok) => {
                setCopied(ok);
                if (ok) window.setTimeout(() => setCopied(false), 1500);
              })
            }
          >
            {copied ? 'Copied' : 'Copy'}
          </button>
        </dd>
        <dt>Author</dt>
        <dd>
          {commit?.author ?? row.author} {commit && <span className="muted">{commit.email}</span>}
        </dd>
        <dt>Date</dt>
        <dd>{new Date((commit?.time ?? row.time) * 1000).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}</dd>
        {commit && commit.parents.length > 0 && (
          <>
            <dt>{commit.parents.length === 1 ? 'Parent' : 'Parents'}</dt>
            <dd className="graph-parents">
              {commit.parents.map((p) => (
                <CommitLink key={p} hash={p} rows={rows} rowOf={rowOf} onSelect={onSelect} />
              ))}
            </dd>
          </>
        )}
      </dl>
      {body && <div className="graph-body-text">{body}</div>}
    </>
  );
}

function WipDetail({ page, row, rowOf, rows, onSelect, onError, onExpired }: DetailProps) {
  const path = row.hash.slice(4);
  const wt = page.worktrees.find((w) => samePath(w.path, path));
  const [changes, setChanges] = useState<GraphChangeDto[] | null>(null);

  useEffect(() => {
    if (!wt) return;
    request('graph.changes', { token: page.token, path: wt.path })
      .then(setChanges)
      .catch((e: Error) => (e.message.includes('expired') ? onExpired() : onError(e.message)));
  }, [page.token, wt, onError, onExpired]);

  return (
    <>
      <h2>{plural(changes?.length ?? wt?.changes ?? 0, 'uncommitted change')}</h2>
      <p className="muted">
        In {wt?.name ?? path}
        {wt?.head && (
          <>
            , on top of <CommitLink hash={wt.head} rows={rows} rowOf={rowOf} onSelect={onSelect} inline />
          </>
        )}
      </p>
      {changes === null ? (
        <p className="empty">Reading changes...</p>
      ) : (
        <ul className="plain graph-files">
          {changes.map((c) => (
            <li key={c.path}>
              <span className={`graph-change change-${c.kind === '?' ? 'new' : c.kind}`}>{c.kind}</span>
              <span className="mono">{c.path}</span>
            </li>
          ))}
        </ul>
      )}
      {changes && changes.length > 0 && <p className="muted graph-legend">M changed · A added · D deleted · R renamed · U conflicted · ? not tracked yet</p>}
    </>
  );
}

function StashDetail({ page, row, rows, rowOf, onSelect }: DetailProps) {
  const st = page.stashes.find((s) => s.rowHash === row.hash);
  if (!st) return <p className="empty">This stash has gone.</p>;
  return (
    <>
      <h2>{st.text}</h2>
      <div className="graph-detail-refs">
        <span className="graph-ref ref-stash">{st.selector}</span>
      </div>
      <dl className="graph-kv">
        <dt>Made on</dt>
        <dd>
          <CommitLink hash={st.base} rows={rows} rowOf={rowOf} onSelect={onSelect} />
        </dd>
        <dt>Date</dt>
        <dd>{new Date(st.time * 1000).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}</dd>
        <dt>Message</dt>
        <dd>{st.message}</dd>
      </dl>
    </>
  );
}

/**
 * Commit messages are usually wrapped at 72 columns for a terminal; in a narrow panel that leaves ragged half-lines.
 * Joins the lines of each prose paragraph, but leaves a paragraph alone when any line is a list item or indented
 * (code, trailers), since there the line breaks mean something.
 */
function reflow(text: string): string {
  const structured = /^(\s|[-*+] |\d+[.)] |[\w-]+: )/;
  return text
    .split(/\n{2,}/)
    .map((p) => {
      const lines = p.split('\n');
      return lines.some((l) => structured.test(l)) ? p : lines.map((l) => l.trim()).join(' ');
    })
    .join('\n\n');
}

async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // Older WebView2 builds refuse the async clipboard without a permission prompt; a selection copy still works.
    const area = document.createElement('textarea');
    area.value = text;
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand('copy');
    area.remove();
    return ok;
  }
}
