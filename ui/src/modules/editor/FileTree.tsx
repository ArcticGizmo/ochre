import { useMemo } from 'react';
import type { EditorFileListDto } from '../../bridge';
import { folderList, isExpanded, leaf, toggleDir } from './store';

interface Dir {
  name: string;
  path: string;
  dirs: Map<string, Dir>;
  files: string[];
}

// Built once per list the host sent; a folder can hold 20,000 paths.
const built = new WeakMap<EditorFileListDto, Dir>();

function treeOf(list: EditorFileListDto): Dir {
  const hit = built.get(list);
  if (hit) return hit;
  const root: Dir = { name: '', path: '', dirs: new Map(), files: [] };
  for (const p of list.paths) {
    const parts = p.split('/');
    let d = root;
    for (let i = 0; i < parts.length - 1; i++) {
      let next = d.dirs.get(parts[i]);
      if (!next) d.dirs.set(parts[i], (next = { name: parts[i], path: parts.slice(0, i + 1).join('/'), dirs: new Map(), files: [] }));
      d = next;
    }
    d.files.push(p);
  }
  built.set(list, root);
  return root;
}

const byName = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'base', numeric: true });

export interface TreeFolder {
  path: string;
  worktree: boolean;
}

interface Props {
  taskId: string;
  folders: TreeFolder[];
  /** The open tab's file, highlighted. */
  active: { folder: string; path: string } | null;
  /** Change letters by folder (lower-cased) and path, from the Changes list. */
  letters?: Map<string, Map<string, string>>;
  onOpen: (folder: string, path: string) => void;
}

const lower = (s: string) => s.replace(/[\\/]+$/, '').toLowerCase();

/** The task's folders as roots, each listed by the host when first opened; only open directories are drawn. */
export function FileTree({ taskId, folders, active, letters, onOpen }: Props) {
  return (
    <div className="file-tree" role="tree" aria-label="Files">
      {folders.map((f) => {
        const open = isExpanded(taskId, f.path, '');
        const state = folderList(f.path);
        return (
          <div key={f.path} role="group">
            <button type="button" className="ft-row ft-root" title={f.path} aria-expanded={open} onClick={() => toggleDir(taskId, f.path, '')}>
              <span className="ft-chev" aria-hidden="true">
                {open ? '▾' : '▸'}
              </span>
              {leaf(f.path)}
              {f.worktree && <span className="ft-tag">worktree</span>}
            </button>
            {open && state?.error && <p className="ft-note danger-text">{state.error}</p>}
            {open && !state?.list && state?.loading && <p className="ft-note muted">Listing files…</p>}
            {open && state?.list && (
              <>
                <DirRows
                  taskId={taskId}
                  folder={f.path}
                  dir={treeOf(state.list)}
                  depth={1}
                  active={active && lower(active.folder) === lower(f.path) ? active.path : null}
                  letters={letters?.get(lower(f.path))}
                  onOpen={onOpen}
                />
                {state.list.paths.length === 0 && <p className="ft-note muted">No files.</p>}
                {state.list.truncated && <p className="ft-note muted">Showing the first {state.list.paths.length.toLocaleString()} files.</p>}
              </>
            )}
          </div>
        );
      })}
    </div>
  );
}

function DirRows({
  taskId,
  folder,
  dir,
  depth,
  active,
  letters,
  onOpen,
}: {
  taskId: string;
  folder: string;
  dir: Dir;
  depth: number;
  active: string | null;
  letters?: Map<string, string>;
  onOpen: (folder: string, path: string) => void;
}) {
  const dirs = useMemo(() => [...dir.dirs.values()].sort((a, b) => byName(a.name, b.name)), [dir]);
  const files = useMemo(() => [...dir.files].sort((a, b) => byName(leaf(a), leaf(b))), [dir]);
  const pad = { paddingLeft: 10 + depth * 16 };
  return (
    <>
      {dirs.map((d) => {
        const open = isExpanded(taskId, folder, d.path);
        return (
          <div key={d.path} role="group">
            <button type="button" className="ft-row" style={pad} aria-expanded={open} onClick={() => toggleDir(taskId, folder, d.path)}>
              <span className="ft-chev" aria-hidden="true">
                {open ? '▾' : '▸'}
              </span>
              {d.name}
            </button>
            {open && <DirRows taskId={taskId} folder={folder} dir={d} depth={depth + 1} active={active} letters={letters} onOpen={onOpen} />}
          </div>
        );
      })}
      {files.map((p) => {
        const letter = letters?.get(p);
        return (
          <button key={p} type="button" className={p === active ? 'ft-row ft-file on' : 'ft-row ft-file'} style={pad} title={p} onClick={() => onOpen(folder, p)}>
            <span className="ft-chev" aria-hidden="true" />
            <span className="ft-name">{leaf(p)}</span>
            {letter && <span className={`ft-letter letter-${letter === '?' ? 'u' : letter.toLowerCase()}`}>{letter}</span>}
          </button>
        );
      })}
    </>
  );
}
