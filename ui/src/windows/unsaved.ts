/** A file with edits that exist only in this window. */
export interface UnsavedFile {
  key: string;
  name: string;
  /** Where it is, for the list: its folder and directory. */
  detail: string;
  /** Full path, for the tooltip. */
  title: string;
}

/** Why a file couldn't be saved when closing. */
export interface SaveFailure {
  name: string;
  reason: string;
}

/** Something in the page holding unsaved edits (the editor). */
export interface UnsavedSource {
  files(): UnsavedFile[];
  /** Saves every file; resolves with the ones it couldn't (changed on disk, read-only, gone). */
  saveAll(): Promise<SaveFailure[]>;
  /** Forgets the edits; the window is about to close. */
  discardAll(): void;
}

const sources = new Set<UnsavedSource>();
const listeners = new Set<() => void>();
let version = 0;

/**
 * Closing a window or quitting asks first while any source holds unsaved files (the host refuses the close and sends
 * window.closeRequested). Sources call `changed` whenever their list may have changed.
 */
export function registerUnsaved(source: UnsavedSource): () => void {
  sources.add(source);
  changed();
  return () => {
    sources.delete(source);
    changed();
  };
}

export function changed() {
  version++;
  listeners.forEach((l) => l());
}

export function subscribeUnsaved(cb: () => void): () => void {
  listeners.add(cb);
  return () => listeners.delete(cb);
}

export const unsavedVersion = () => version;

export const unsavedFiles = (): UnsavedFile[] => [...sources].flatMap((s) => s.files());

export async function saveAllUnsaved(): Promise<SaveFailure[]> {
  const results = await Promise.all([...sources].map((s) => s.saveAll()));
  return results.flat();
}

export function discardAllUnsaved() {
  sources.forEach((s) => s.discardAll());
}
