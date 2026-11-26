import { useEffect, useState } from 'react';
import { request, type WorktreePlanDto, type WorktreeProviderDto } from '../bridge';
import { PathText } from '../PathText';

export interface NewWorktree {
  repo: string;
  provider: string;
  branch: string;
}

interface Props {
  taskId: string;
  repos: string[];
  providers: WorktreeProviderDto[];
  value: NewWorktree;
  onChange: (value: NewWorktree) => void;
}

/**
 * The fields for a new worktree inside a start dialog: which repo, which kind (when more than one module offers
 * one), the branch, and where it will land before anything is made.
 */
export function NewWorktreeFields({ taskId, repos, providers, value, onChange }: Props) {
  const [plan, setPlan] = useState<WorktreePlanDto | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  // Ask where it would go once typing pauses; the host also checks the branch name.
  useEffect(() => {
    if (!value.repo || !value.provider || !value.branch.trim()) {
      setPlan(null);
      setProblem(null);
      return;
    }
    let alive = true;
    const timer = setTimeout(() => {
      request('worktrees.plan', { taskId, repo: value.repo, provider: value.provider, branch: value.branch })
        .then((p) => {
          if (!alive) return;
          setPlan(p);
          setProblem(null);
        })
        .catch((e: Error) => {
          if (!alive) return;
          setPlan(null);
          setProblem(e.message);
        });
    }, 300);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [taskId, value.repo, value.provider, value.branch]);

  return (
    <>
      {repos.length > 1 && (
        <label className="field">
          <span>From repo</span>
          <select value={value.repo} onChange={(e) => onChange({ ...value, repo: e.target.value })}>
            {repos.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </label>
      )}
      {providers.length > 1 && (
        <label className="field">
          <span>Kind</span>
          <select value={value.provider} onChange={(e) => onChange({ ...value, provider: e.target.value })}>
            {providers.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className="field">
        <span>Branch</span>
        <input className="mono" value={value.branch} maxLength={100} onChange={(e) => onChange({ ...value, branch: e.target.value })} />
        {plan && (
          <small className="muted worktree-plan">
            {plan.newBranch ? `New branch from ${plan.base ?? 'the current commit'}, in ` : 'Checks out the existing branch in '}
            {plan.path ? <PathText path={plan.path} /> : `a ${providers.find((p) => p.id === value.provider)?.name.toLowerCase() ?? 'folder'} it picks`}
          </small>
        )}
        {problem && <small className="error">{problem}</small>}
      </label>
    </>
  );
}
