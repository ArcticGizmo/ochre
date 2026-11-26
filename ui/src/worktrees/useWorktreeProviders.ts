import { useEffect, useState } from 'react';
import { request, type WorktreeProviderDto } from '../bridge';

// Providers come from modules, which only change when Ochre restarts, so one answer serves the window's life.
let cached: Promise<WorktreeProviderDto[]> | null = null;

/** The worktree providers the running modules offer (ADR 0012); empty with every such module off. */
export function useWorktreeProviders(): WorktreeProviderDto[] {
  const [providers, setProviders] = useState<WorktreeProviderDto[]>([]);
  useEffect(() => {
    let alive = true;
    cached ??= request('worktrees.providers', {}).catch(() => {
      cached = null;
      return [];
    });
    void cached.then((p) => alive && setProviders(p));
    return () => {
      alive = false;
    };
  }, []);
  return providers;
}

/** A branch name for the task's new worktree: its id and the start of its title, e.g. t-0042-fix-login. */
export function defaultBranch(taskId: string, title: string): string {
  const slug = title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
    .replace(/-+$/, '');
  return slug ? `${taskId.toLowerCase()}-${slug}` : taskId.toLowerCase();
}
