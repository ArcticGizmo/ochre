import type { editor } from 'monaco-editor/editor/editor.api';
import { on, request, type EditorChangedEvent, type EditorFileDto, type EditorFileListDto } from '../../bridge';
import { loadMonaco } from '../../editor/loadMonaco';
import { changed as unsavedChanged, registerUnsaved, type SaveFailure } from '../../windows/unsaved';

// The Files view's state for this window: open tabs (with their Monaco models, which hold unsaved edits), which tab
// each task shows, and the folder lists behind the tree. It lives outside React so switching to another view, or
// to another task, keeps every tab and edit as it was.

export type MdMode = 'source' | 'preview' | 'split';

export interface Tab {
  key: string;
  taskId: string;
  folder: string;
  path: string;
  name: string;
  /** Null until the first read answers. */
  file: EditorFileDto | null;
  error: string | null;
  /** The version on disk the buffer was read or saved at; a save names it. */
  version: string;
  model: editor.ITextModel | null;
  language: string;
  savedAlt: number;
  dirty: boolean;
  saving: boolean;
  /** Changed on disk while dirty, or a save refused because it had: the version there now ("" when deleted). */
  disk: { version: string; by: string | null } | null;
  /** The text on disk, shown against the buffer while comparing. */
  compare: { text: string } | null;
  mdMode: MdMode;
  viewState: editor.ICodeEditorViewState | null;
  reveal: { line: number; at: number } | null;
  closed: boolean;
}

interface TaskTabs {
  tabs: Tab[];
  active: string | null;
}

export interface FolderList {
  list: EditorFileListDto | null;
  loading: boolean;
  error: string | null;
}

const byTask = new Map<string, TaskTabs>();
const lists = new Map<string, FolderList>();
const expanded = new Map<string, Set<string>>();
const subs = new Set<() => void>();
let ver = 0;

function emit() {
  ver++;
  subs.forEach((s) => s());
}

export const subscribe = (cb: () => void) => {
  subs.add(cb);
  wire();
  return () => {
    subs.delete(cb);
  };
};
export const storeVersion = () => ver;

const lower = (s: string) => s.replace(/[\\/]+$/, '').toLowerCase();
export const tabKey = (folder: string, path: string) => `${lower(folder)}\n${path}`;
export const leaf = (p: string) => p.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? p;
export const isMarkdown = (path: string) => /\.(md|markdown|mdx)$/i.test(path);

const empty: TaskTabs = { tabs: [], active: null };
export const tabsOf = (taskId: string): TaskTabs => byTask.get(taskId) ?? empty;
export const activeTab = (taskId: string): Tab | null => {
  const t = byTask.get(taskId);
  return t?.tabs.find((x) => x.key === t.active) ?? null;
};
const allTabs = () => [...byTask.values()].flatMap((t) => t.tabs);

// Which agent probably changed a file: set by the view, which knows the sessions.
let guessAuthor: (taskId: string) => string | null = () => null;
export function setAuthorGuess(fn: (taskId: string) => string | null) {
  guessAuthor = fn;
}

export function openFile(taskId: string, folder: string, path: string, line?: number) {
  let t = byTask.get(taskId);
  if (!t) byTask.set(taskId, (t = { tabs: [], active: null }));
  const key = tabKey(folder, path);
  let tab = t.tabs.find((x) => x.key === key);
  if (!tab) {
    tab = {
      key,
      taskId,
      folder,
      path,
      name: leaf(path),
      file: null,
      error: null,
      version: '',
      model: null,
      language: 'plaintext',
      savedAlt: 0,
      dirty: false,
      saving: false,
      disk: null,
      compare: null,
      mdMode: isMarkdown(path) ? 'split' : 'source',
      viewState: null,
      reveal: null,
      closed: false,
    };
    t.tabs.push(tab);
    void load(tab);
    scheduleWatch();
  }
  t.active = key;
  if (line) tab.reveal = { line, at: Date.now() };
  emit();
}

export function activate(taskId: string, key: string) {
  const t = byTask.get(taskId);
  if (!t || t.active === key) return;
  t.active = key;
  emit();
}

const read = (tab: Tab) => request('editor.read', { taskId: tab.taskId, folder: tab.folder, path: tab.path });

async function load(tab: Tab) {
  try {
    const [file, m] = await Promise.all([read(tab), loadMonaco()]);
    if (tab.closed) return;
    tab.file = file;
    tab.version = file.version;
    if (file.text !== null) {
      tab.language = m.languageFor(tab.path);
      const model = m.monaco.editor.createModel(file.text, tab.language);
      tab.model = model;
      tab.savedAlt = model.getAlternativeVersionId();
      model.onDidChangeContent(() => {
        const dirty = model.getAlternativeVersionId() !== tab.savedAlt;
        if (dirty === tab.dirty) return;
        tab.dirty = dirty;
        unsavedChanged();
        emit();
      });
    }
  } catch (e) {
    tab.error = e instanceof Error ? e.message : String(e);
  }
  emit();
}

// Replaces the buffer as an edit, so undo can still bring back what was there.
function replaceText(tab: Tab, text: string) {
  const model = tab.model;
  if (!model) return;
  if (model.getValue() !== text) model.pushEditOperations([], [{ range: model.getFullModelRange(), text }], () => null);
  tab.savedAlt = model.getAlternativeVersionId();
  tab.dirty = false;
}

/** Reads the file again and replaces the buffer: an outside change to a clean tab, or Reload (discarding edits). */
export async function reload(tab: Tab) {
  try {
    const file = await read(tab);
    tab.file = file;
    tab.version = file.version;
    if (file.text !== null) replaceText(tab, file.text);
    tab.disk = null;
    tab.compare = null;
    tab.error = null;
  } catch (e) {
    tab.error = e instanceof Error ? e.message : String(e);
  }
  unsavedChanged();
  emit();
}

/** Saves the buffer if the file is still the version it was read at. Resolves with why not, or null when saved. */
export async function save(tab: Tab): Promise<string | null> {
  const model = tab.model;
  if (!model || !tab.file) return 'it hasn’t loaded';
  if (tab.file.readOnly) return 'it’s read-only here';
  if (!tab.dirty) return null;
  if (tab.disk) return tab.disk.version === '' ? 'it was deleted' : 'it changed on disk';
  if (tab.saving) return 'it’s already saving';
  tab.saving = true;
  emit();
  const alt = model.getAlternativeVersionId();
  try {
    const r = await request('editor.write', { taskId: tab.taskId, folder: tab.folder, path: tab.path, text: model.getValue(), version: tab.version });
    if (r.saved) {
      tab.version = r.version;
      tab.savedAlt = alt;
      tab.dirty = model.getAlternativeVersionId() !== alt;
      return null;
    }
    tab.disk = { version: r.version, by: guessAuthor(tab.taskId) };
    return r.version === '' ? 'it was deleted' : 'it changed on disk';
  } catch (e) {
    return e instanceof Error ? e.message : String(e);
  } finally {
    tab.saving = false;
    unsavedChanged();
    emit();
  }
}

/** Keep mine: save over the change on disk (the user confirmed). */
export async function keepMine(tab: Tab): Promise<string | null> {
  if (!tab.disk) return save(tab);
  if (tab.disk.version === '') return 'it was deleted, and Ochre doesn’t create files';
  tab.version = tab.disk.version;
  tab.disk = null;
  tab.compare = null;
  return save(tab);
}

/** Shows (or hides) the file on disk against the buffer. */
export async function toggleCompare(tab: Tab) {
  if (tab.compare) {
    tab.compare = null;
    emit();
    return;
  }
  try {
    const file = await read(tab);
    tab.compare = { text: file.text ?? '' };
  } catch (e) {
    tab.error = e instanceof Error ? e.message : String(e);
  }
  emit();
}

export function setMdMode(tab: Tab, mode: MdMode) {
  tab.mdMode = mode;
  emit();
}

/** Closes a tab, dropping any unsaved edits (the view asks first). */
export function closeTab(taskId: string, key: string) {
  const t = byTask.get(taskId);
  const i = t?.tabs.findIndex((x) => x.key === key) ?? -1;
  if (!t || i < 0) return;
  const [tab] = t.tabs.splice(i, 1);
  tab.closed = true;
  tab.model?.dispose();
  if (t.active === key) t.active = t.tabs[Math.min(i, t.tabs.length - 1)]?.key ?? null;
  scheduleWatch();
  unsavedChanged();
  emit();
}

function onChanged(e: EditorChangedEvent) {
  for (const tab of allTabs()) {
    if (tab.taskId !== e.taskId || tab.key !== tabKey(e.folder, e.path)) continue;
    // Our own save, or a write that left the bytes as they were.
    if (e.version === tab.version) continue;
    // The save's answer decides; look again once it's in.
    if (tab.saving) {
      setTimeout(() => onChanged(e), 300);
      continue;
    }
    if (!tab.dirty && !tab.disk) {
      void reload(tab);
      continue;
    }
    tab.disk = { version: e.version, by: tab.disk?.by ?? guessAuthor(tab.taskId) };
    emit();
  }
}

let watchTimer = 0;
function scheduleWatch() {
  clearTimeout(watchTimer);
  watchTimer = window.setTimeout(() => {
    const files = allTabs()
      .slice(0, 64)
      .map((t) => ({ taskId: t.taskId, folder: t.folder, path: t.path }));
    request('editor.watch', { files }).catch(() => {});
  }, 50);
}

// ---- the tree's folder lists and which of its folders are open ----

export const folderList = (folder: string): FolderList | undefined => lists.get(lower(folder));

export function loadList(taskId: string, folder: string, force = false) {
  const k = lower(folder);
  const cur = lists.get(k);
  if (cur && (cur.loading || (cur.list && !force))) return;
  lists.set(k, { list: cur?.list ?? null, loading: true, error: null });
  emit();
  request('editor.list', { taskId, folder })
    .then((list) => lists.set(k, { list, loading: false, error: null }))
    .catch((e: Error) => lists.set(k, { list: cur?.list ?? null, loading: false, error: e.message }))
    .finally(emit);
}

const nodeKey = (folder: string, dir: string) => `${lower(folder)}\n${dir}`;
const openSet = (taskId: string) => {
  let s = expanded.get(taskId);
  if (!s) expanded.set(taskId, (s = new Set()));
  return s;
};

export const isExpanded = (taskId: string, folder: string, dir: string) => expanded.get(taskId)?.has(nodeKey(folder, dir)) ?? false;
export const hasExpanded = (taskId: string) => (expanded.get(taskId)?.size ?? 0) > 0;

export function toggleDir(taskId: string, folder: string, dir: string) {
  const s = openSet(taskId);
  const k = nodeKey(folder, dir);
  if (s.has(k)) s.delete(k);
  else s.add(k);
  if (dir === '') loadList(taskId, folder);
  emit();
}

/** Opens a folder's root, and every directory down to `path` when given, so a file shows in the tree. */
export function revealInTree(taskId: string, folder: string, path?: string) {
  const s = openSet(taskId);
  s.add(nodeKey(folder, ''));
  if (path) {
    const parts = path.split('/');
    for (let i = 1; i < parts.length; i++) s.add(nodeKey(folder, parts.slice(0, i).join('/')));
  }
  loadList(taskId, folder);
  emit();
}

/** New files from agents show up when the user comes back to the window. */
function refreshOpenLists() {
  for (const [taskId, open] of expanded) {
    for (const [k, entry] of lists) {
      if (entry.list && open.has(`${k}\n`)) loadList(taskId, entry.list.folder, true);
    }
  }
}

let wired = false;
function wire() {
  if (wired) return;
  wired = true;
  on('editor.changed', onChanged);
  window.addEventListener('focus', refreshOpenLists);
  registerUnsaved({
    files: () =>
      allTabs()
        .filter((t) => t.dirty)
        .map((t) => {
          const dir = t.path.includes('/') ? t.path.slice(0, t.path.lastIndexOf('/')) : '';
          return { key: `${t.taskId}\n${t.key}`, name: t.name, detail: dir ? `${leaf(t.folder)} › ${dir}` : leaf(t.folder), title: `${t.folder}\\${t.path.replaceAll('/', '\\')}` };
        }),
    saveAll: async () => {
      const failures: SaveFailure[] = [];
      for (const t of allTabs().filter((x) => x.dirty)) {
        const reason = await save(t);
        if (reason) failures.push({ name: t.name, reason });
      }
      return failures;
    },
    discardAll: () => {
      for (const t of allTabs()) {
        if (!t.model || !t.dirty) continue;
        t.savedAlt = t.model.getAlternativeVersionId();
        t.dirty = false;
      }
      unsavedChanged();
      emit();
    },
  });
}

// The unsaved list must reach the host even before the Files view is first shown.
wire();
