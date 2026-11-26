import { useState } from 'react';
import { request, type WorktreeDto } from '../bridge';
import { NewWorktreeFields, type NewWorktree } from './NewWorktreeFields';
import { defaultBranch, useWorktreeProviders } from './useWorktreeProviders';

const OTHER = '__other__';
const NEW = '__new__';

interface Options {
  taskId: string | null;
  title: string;
  repos: string[];
  worktrees: WorktreeDto[];
  /** An empty "another folder" is allowed (a terminal opens in the home folder). */
  otherMayBeEmpty?: boolean;
  otherPlaceholder: string;
}

/**
 * Where an agent or terminal starts: one of the task's repos or worktrees, a new worktree for the task (when a
 * module offers one), or any folder. A new worktree is made only when the dialog submits, and only once, so a
 * retry (after the trust question, or a failed start) reuses it.
 */
export function useFolderChoice({ taskId, title, repos, worktrees, otherMayBeEmpty, otherPlaceholder }: Options) {
  const providers = useWorktreeProviders();
  const [folder, setFolder] = useState(OTHER);
  const [other, setOther] = useState('');
  const [made, setMade] = useState<NewWorktree | null>(null);
  const [created, setCreated] = useState<string | null>(null);
  const [newWorktree, setNewWorktree] = useState<NewWorktree>({ repo: '', provider: '', branch: '' });

  const existing = worktrees.filter((w) => w.exists);
  const canMake = taskId !== null && providers.length > 0 && repos.length > 0;
  // The task remembers its last choice by what it made last; providers arrive after the dialog opens, so this is
  // worked out on each render until the user picks.
  const latest = [...worktrees].sort((a, b) => (b.created ?? '').localeCompare(a.created ?? ''))[0]?.provider;
  const preferred = providers.find((p) => p.id === latest)?.id ?? providers[0]?.id ?? '';
  const wanted: NewWorktree = { ...newWorktree, repo: newWorktree.repo || repos[0] || '', provider: newWorktree.provider || preferred };

  function reset(first: string | null) {
    setFolder(first ?? repos[0] ?? OTHER);
    setOther('');
    setMade(null);
    setCreated(null);
    setNewWorktree({ repo: repos[0] ?? '', provider: '', branch: taskId ? defaultBranch(taskId, title) : '' });
  }

  function choose(value: string) {
    setFolder(value);
    setCreated(null);
    setMade(null);
  }

  const ready = folder === NEW ? canMake && wanted.branch.trim().length > 0 : folder === OTHER ? otherMayBeEmpty || other.trim().length > 0 : true;

  /** The folder to start in, making the new worktree first if that's the choice. Throws the host's refusal. */
  async function resolve(): Promise<string> {
    if (folder === OTHER) return other.trim();
    if (folder !== NEW) return folder;
    if (created && made && JSON.stringify(made) === JSON.stringify(wanted)) return created;
    const w = await request('worktrees.create', { taskId: taskId!, repo: wanted.repo, provider: wanted.provider, branch: wanted.branch.trim() });
    setMade(wanted);
    setCreated(w.path);
    return w.path;
  }

  const fields = (
    <>
      <label className="field">
        <span>Folder</span>
        <select value={folder === NEW && !canMake ? OTHER : folder} onChange={(e) => choose(e.target.value)}>
          {repos.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
          {existing.map((w) => (
            <option key={w.path} value={w.path}>
              {w.path}
              {w.branch ? ` (${w.branch})` : ''}
            </option>
          ))}
          {canMake && <option value={NEW}>A new worktree for this task...</option>}
          <option value={OTHER}>{repos.length || existing.length ? 'Another folder...' : 'A folder of your choice'}</option>
        </select>
      </label>
      {folder === NEW && canMake && taskId && <NewWorktreeFields taskId={taskId} repos={repos} providers={providers} value={wanted} onChange={setNewWorktree} />}
      {folder === OTHER && (
        <label className="field">
          <span>Folder path</span>
          <input value={other} onChange={(e) => setOther(e.target.value)} placeholder={otherPlaceholder} />
        </label>
      )}
    </>
  );

  return { fields, ready, reset, resolve, isNew: folder === NEW };
}
