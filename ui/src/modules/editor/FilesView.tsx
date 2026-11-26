import { useCallback, useEffect, useRef, useState, useSyncExternalStore, type MouseEvent } from 'react';
import { request, type TaskListItem } from '../../bridge';
import { ConfirmDialog, Dialog } from '../../Dialog';
import { CodeView, DiffView } from '../../editor/CodeView';
import { useMonaco } from '../../editor/loadMonaco';
import { renderMarkdown } from '../../markdown';
import { Menu } from '../../Menu';
import { useSessions } from '../../sessions/useSessions';
import { sessionName, StatePill } from '../../tasks/present';
import type { AppContext } from '../types';
import { FileTree, type TreeFolder } from './FileTree';
import {
  activate,
  activeTab,
  closeTab,
  hasExpanded,
  isMarkdown,
  keepMine,
  leaf,
  openFile,
  reload,
  revealInTree,
  save,
  setAuthorGuess,
  setMdMode,
  storeVersion,
  subscribe,
  tabsOf,
  toggleCompare,
  type MdMode,
  type Tab,
} from './store';

/** What the Files view is asked to show: a task, and optionally a folder to open in the tree or a file to open. */
export interface FilesTarget {
  task: string;
  folder?: string;
  path?: string;
  line?: number;
  /** Makes a repeated request (the same file again) a new parameter. */
  at?: number;
}

export const filesParam = (t: FilesTarget) => JSON.stringify(t);

function parseParam(param: string | null): FilesTarget | null {
  if (!param) return null;
  try {
    const v = JSON.parse(param) as Partial<FilesTarget>;
    if (typeof v.task !== 'string') return null;
    return {
      task: v.task,
      folder: typeof v.folder === 'string' ? v.folder : undefined,
      path: typeof v.path === 'string' ? v.path : undefined,
      line: typeof v.line === 'number' ? v.line : undefined,
    };
  } catch {
    return null;
  }
}

const foldersOf = (t: TaskListItem | undefined): TreeFolder[] =>
  t ? [...t.repos.map((path) => ({ path, worktree: false })), ...t.worktrees.map((w) => ({ path: w.path, worktree: true }))] : [];

/** The editor module's title-bar view: a task's folders, its open files, and the one being edited. */
export function FilesView({ param, setParam, ctx }: { param: string | null; setParam: (p: string | null) => void; ctx: AppContext }) {
  useSyncExternalStore(subscribe, storeVersion);
  const target = parseParam(param);
  const withFolders = ctx.tasks.filter((t) => t.repos.length + t.worktrees.length > 0);
  const ordered = [...withFolders.filter((t) => t.state !== 'done'), ...withFolders.filter((t) => t.state === 'done')];
  const taskId = target?.task ?? ordered[0]?.id ?? null;
  const task = ctx.tasks.find((t) => t.id === taskId);
  const folders = foldersOf(task);

  // Which agent probably changed a file: the one agent working on the task when the change arrived, if only one was.
  const live = useSessions();
  const liveRef = useRef(live);
  liveRef.current = live;
  useEffect(() => {
    setAuthorGuess((id) => {
      const working = liveRef.current.filter((s) => s.taskId === id && s.status === 'busy');
      return working.length === 1 ? sessionName(working[0]) : null;
    });
  }, []);

  // A request to open something arrives as a new parameter; act on each one once.
  const handled = useRef<string | null>(null);
  useEffect(() => {
    if (!param || handled.current === param || !target) return;
    handled.current = param;
    if (target.folder && target.path) {
      revealInTree(target.task, target.folder, target.path);
      openFile(target.task, target.folder, target.path, target.line);
    } else if (target.folder) revealInTree(target.task, target.folder);
  }, [param, target]);

  // A task seen for the first time opens its first folder, so the tree isn't a column of closed roots.
  useEffect(() => {
    if (taskId && folders.length > 0 && !hasExpanded(taskId)) revealInTree(taskId, folders[0].path);
  }, [taskId, folders]);

  const [closing, setClosing] = useState<Tab | null>(null);
  const [overwriting, setOverwriting] = useState<Tab | null>(null);
  const { onError } = ctx;

  const doSave = useCallback(
    (tab: Tab) => {
      save(tab).then((why) => why && !tab.disk && onError(`Couldn't save ${tab.name}: ${why}`));
    },
    [onError],
  );

  if (!task || !taskId) {
    return (
      <div className="files-view">
        <p className="empty files-empty">No task has a folder yet. Add a repo to a task to browse and edit its files here.</p>
      </div>
    );
  }

  const { tabs } = tabsOf(taskId);
  const active = activeTab(taskId);

  const requestClose = (tab: Tab) => (tab.dirty ? setClosing(tab) : closeTab(taskId, tab.key));

  return (
    <div className="files-view">
      <div className="graph-toolbar">
        <Menu
          buttonClass="files-task"
          label={
            <>
              <span className="files-task-title">{task.title}</span>
              <span className="muted mono-sm">{task.id}</span>
              <StatePill state={task.state} small />
            </>
          }
          ariaLabel="Choose a task"
          items={ordered.map((t) => ({ key: t.id, label: `${t.id} ${t.title}`, current: t.id === taskId, onSelect: () => setParam(filesParam({ task: t.id })) }))}
        />
        <button className="link" onClick={() => ctx.openTask(taskId)}>
          Open task
        </button>
        <span className="spacer" />
      </div>

      <div className="files-body">
        <aside className="files-side" aria-label={`Files of ${taskId}`}>
          <div className="files-side-tabs" role="tablist">
            <button type="button" role="tab" aria-selected="true" className="files-side-tab">
              Files
            </button>
          </div>
          <FileTree
            taskId={taskId}
            folders={folders}
            active={active ? { folder: active.folder, path: active.path } : null}
            onOpen={(folder, path) => openFile(taskId, folder, path)}
          />
        </aside>

        <main className="files-main" aria-label="Editor">
          {tabs.length > 0 && (
            <div className="files-tabs" role="tablist">
              {tabs.map((t) => (
                <div
                  key={t.key}
                  role="tab"
                  aria-selected={t.key === active?.key}
                  className={t.key === active?.key ? 'files-tab on' : 'files-tab'}
                  title={`${t.folder}\\${t.path.replaceAll('/', '\\')}`}
                  onClick={() => activate(taskId, t.key)}
                  onAuxClick={(e) => e.button === 1 && requestClose(t)}
                >
                  {t.name}
                  {t.dirty ? (
                    <button type="button" className="files-tab-x dirty" aria-label={`Close ${t.name} (unsaved)`} onClick={(e) => (e.stopPropagation(), requestClose(t))}>
                      <span className="unsaved-dot" />
                    </button>
                  ) : (
                    <button type="button" className="files-tab-x" aria-label={`Close ${t.name}`} onClick={(e) => (e.stopPropagation(), requestClose(t))}>
                      <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
                        <path d="M2 2l6 6M8 2l-6 6" stroke="currentColor" strokeWidth="1.4" />
                      </svg>
                    </button>
                  )}
                </div>
              ))}
            </div>
          )}
          {active ? (
            <TabPane key={active.key} tab={active} onSave={() => doSave(active)} onKeepMine={() => setOverwriting(active)} onClose={() => requestClose(active)} />
          ) : (
            <p className="empty files-empty">Open a file from the task's folders on the left.</p>
          )}
        </main>
      </div>

      <Dialog
        open={closing !== null}
        title={`Close ${closing?.name ?? ''}?`}
        onClose={() => setClosing(null)}
        onSubmit={() => {
          const tab = closing;
          setClosing(null);
          if (tab)
            save(tab).then((why) => {
              if (!why) closeTab(taskId, tab.key);
              else if (!tab.disk) onError(`Couldn't save ${tab.name}: ${why}`);
            });
        }}
        actions={
          <>
            <button type="button" onClick={() => setClosing(null)}>
              Cancel
            </button>
            <button
              type="button"
              className="danger"
              onClick={() => {
                if (closing) closeTab(taskId, closing.key);
                setClosing(null);
              }}
            >
              Discard
            </button>
            <button type="submit" className="primary" autoFocus>
              Save and close
            </button>
          </>
        }
      >
        <p className="muted">It has unsaved edits, which exist only in this window.</p>
      </Dialog>

      <ConfirmDialog
        open={overwriting !== null}
        title="Save over the change on disk?"
        confirmLabel="Save mine"
        danger
        onClose={() => setOverwriting(null)}
        onConfirm={() => {
          const tab = overwriting;
          setOverwriting(null);
          if (tab) keepMine(tab).then((why) => why && onError(`Couldn't save ${tab.name}: ${why}`));
        }}
      >
        <p>
          {overwriting?.name} changed on disk{overwriting?.disk?.by ? `, probably by ${overwriting.disk.by},` : ''} since you opened it. Saving now replaces that change with
          yours.
        </p>
      </ConfirmDialog>
    </div>
  );
}

function TabPane({ tab, onSave, onKeepMine, onClose }: { tab: Tab; onSave: () => void; onKeepMine: () => void; onClose: () => void }) {
  const m = useMonaco();
  const [pos, setPos] = useState({ line: 1, column: 1 });
  const dir = tab.path.includes('/') ? tab.path.slice(0, tab.path.lastIndexOf('/')).split('/') : [];
  const md = isMarkdown(tab.path) && tab.model !== null;
  const file = tab.file;

  let body;
  if (tab.error) body = <p className="empty files-empty danger-text">{tab.error}</p>;
  else if (!file || (file.text !== null && !tab.model)) body = <p className="empty files-empty">Opening {tab.name}…</p>;
  else if (file.problem === 'missing') body = <Problem text="This file isn't there any more." onClose={onClose} />;
  else if (file.problem === 'tooLarge') body = <Problem text={`Too large to show here (${(file.size / 1024 / 1024).toFixed(1)} MB; the limit is 5 MB).`} onClose={onClose} />;
  else if (file.problem === 'binary') body = <Problem text="A binary file. Ochre shows text only." onClose={onClose} />;
  else if (tab.model && tab.compare)
    body = <DiffView original={tab.compare.text} modified={tab.model} language={tab.language} readOnly={file.readOnly} sideBySide onSave={onSave} />;
  else if (tab.model) {
    const code = (
      <CodeView
        model={tab.model}
        readOnly={file.readOnly}
        viewState={tab.viewState}
        onViewState={(s) => (tab.viewState = s)}
        reveal={tab.reveal}
        onSave={onSave}
        onCursor={(line, column) => setPos({ line, column })}
      />
    );
    body =
      md && tab.mdMode !== 'source' ? (
        <div className={tab.mdMode === 'split' ? 'md-split' : 'md-only'}>
          {tab.mdMode === 'split' && code}
          <MarkdownPreview tab={tab} />
        </div>
      ) : (
        code
      );
  }

  const language = m && tab.model ? (m.monaco.languages.getLanguages().find((l) => l.id === tab.language)?.aliases?.[0] ?? tab.language) : '';
  const state = tab.saving ? 'Saving…' : file?.readOnly ? 'Read-only' : tab.dirty ? 'Unsaved' : 'Saved';

  return (
    <div className="files-pane">
      <div className="files-crumbs">
        <span title={tab.folder}>{leaf(tab.folder)}</span>
        {dir.map((d, i) => (
          <span key={i}>
            <span className="sep">›</span>
            {d}
          </span>
        ))}
        <span className="sep">›</span>
        <span className="here">{tab.name}</span>
        <span className="spacer" />
        {md && (
          <div className="seg" role="group" aria-label="Markdown view">
            {(['source', 'preview', 'split'] as MdMode[]).map((mode) => (
              <button key={mode} type="button" aria-pressed={tab.mdMode === mode} onClick={() => setMdMode(tab, mode)}>
                {mode === 'source' ? 'Source' : mode === 'preview' ? 'Preview' : 'Side by side'}
              </button>
            ))}
          </div>
        )}
      </div>

      {tab.disk && (
        <div className="files-notice" role="alert">
          <svg className="files-warn" width="18" height="18" viewBox="0 0 18 18" aria-hidden="true">
            <path d="M9 2l7.5 13h-15z" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
            <path d="M9 7v4M9 13v.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
          </svg>
          <span>
            {tab.disk.version === '' ? (
              <>
                <b>{tab.name} was deleted</b> while you had unsaved edits{tab.disk.by ? <>, probably by <b>{tab.disk.by}</b></> : null}. Copy what you need, then close it.
              </>
            ) : (
              <>
                <b>{tab.name} changed on disk</b> while you had unsaved edits{tab.disk.by ? <>, probably by <b>{tab.disk.by}</b></> : null}.
              </>
            )}
          </span>
          <span className="spacer" />
          {tab.disk.version !== '' && (
            <button type="button" aria-pressed={tab.compare !== null} className={tab.compare ? 'on' : ''} onClick={() => toggleCompare(tab)}>
              Compare
            </button>
          )}
          {tab.disk.version !== '' && (
            <button type="button" className="danger" onClick={() => reload(tab)}>
              Reload, discard mine
            </button>
          )}
          {tab.disk.version !== '' && (
            <button type="button" onClick={onKeepMine}>
              Keep mine…
            </button>
          )}
          {tab.disk.version === '' && (
            <button type="button" className="danger" onClick={onClose}>
              Close
            </button>
          )}
        </div>
      )}
      {tab.compare && (
        <div className="files-compare-head">
          <div>On disk now · read-only</div>
          <div>Yours, unsaved · keep editing here</div>
        </div>
      )}

      <div className="files-content">{body}</div>

      <div className="files-status">
        <span className={tab.dirty ? 'files-state dirty' : 'files-state'}>{state}</span>
        {tab.disk && <span>Saving waits until you choose</span>}
        {!tab.disk && file && !file.readOnly && (
          <span>
            <kbd>Ctrl</kbd> <kbd>S</kbd> save
          </span>
        )}
        <span className="spacer" />
        {tab.model && (
          <span>
            Ln {pos.line}, Col {pos.column}
          </span>
        )}
        {file?.problem === 'notUtf8' ? <span>Not UTF-8, read-only</span> : file && file.text !== null && <span>{file.bom ? 'UTF-8 with BOM' : 'UTF-8'}</span>}
        {tab.model && <span>{tab.model.getEOL() === '\r\n' ? 'CRLF' : 'LF'}</span>}
        {language && <span>{language}</span>}
      </div>
    </div>
  );
}

function Problem({ text, onClose }: { text: string; onClose: () => void }) {
  return (
    <div className="files-empty">
      <p className="empty">{text}</p>
      <button type="button" onClick={onClose}>
        Close it
      </button>
    </div>
  );
}

/** The rendered markdown, following the source as it changes. Links never navigate the window. */
function MarkdownPreview({ tab }: { tab: Tab }) {
  const [html, setHtml] = useState(() => renderMarkdown(tab.model?.getValue() ?? ''));
  useEffect(() => {
    const model = tab.model;
    if (!model) return;
    let timer = 0;
    const sub = model.onDidChangeContent(() => {
      clearTimeout(timer);
      timer = window.setTimeout(() => setHtml(renderMarkdown(model.getValue())), 150);
    });
    return () => {
      clearTimeout(timer);
      sub.dispose();
    };
  }, [tab.model]);

  const follow = (e: MouseEvent, newWindow: boolean) => {
    const a = (e.target as Element).closest('a');
    if (!a) return;
    e.preventDefault();
    const href = a.getAttribute('href') ?? '';
    if (/^https?:\/\//i.test(href)) {
      request('app.openUrl', { url: href, newWindow }).catch(() => {});
      return;
    }
    if (href === '' || href.startsWith('#') || /^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith('/') || href.startsWith('\\')) return;
    const target = resolveRelative(tab.path, href);
    if (target) openFile(tab.taskId, tab.folder, target);
  };

  return (
    <div
      className="md-preview markdown"
      onClick={(e) => follow(e, false)}
      onAuxClick={(e) => e.button === 1 && follow(e, true)}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}

/** A link in a markdown file, relative to it, as a path in the same folder; null when it climbs out of the folder. */
function resolveRelative(from: string, href: string): string | null {
  let rel: string;
  try {
    rel = decodeURIComponent(href.split('#')[0].split('?')[0]);
  } catch {
    return null;
  }
  if (!rel) return null;
  const parts = from.split('/').slice(0, -1);
  for (const seg of rel.replaceAll('\\', '/').split('/')) {
    if (seg === '' || seg === '.') continue;
    if (seg === '..') {
      if (parts.length === 0) return null;
      parts.pop();
    } else parts.push(seg);
  }
  return parts.length ? parts.join('/') : null;
}
