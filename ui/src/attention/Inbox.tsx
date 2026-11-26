import { useEffect, useRef, useState } from 'react';
import { request, type AttentionItemDto, type PermissionDto, type TaskListItem } from '../bridge';
import { renderMarkdown } from '../markdown';
import { sessionName, StatePill, when } from '../tasks/present';
import { useAttention } from './useAttention';

type Filter = 'all' | 'need' | 'done';

// A question answers in place only when it fits on one card; anything bigger goes to the thread.
const MaxInlineOptions = 5;

// How long an answered item stays with its receipt, so the click visibly did something before the row goes.
const LingerMs = 1500;

interface Props {
  tasks: TaskListItem[];
  onOpenTask: (taskId: string) => void;
  onOpenThread: (sessionId: string, taskId: string) => void;
  /** Today lists outside sessions with the tasks they might belong to. */
  onShowOutside: () => void;
}

interface Lingering {
  item: AttentionItemDto;
  receipt: string;
  /** Where it sat in the list, so it stays put while it lingers. */
  at: number;
}

/** The Inbox tab's badge: how many items are blocked on the user. Finished turns alone never add one. */
export function InboxBadge() {
  const { needsYou } = useAttention();
  return needsYou > 0 ? (
    <span className="inbox-badge" aria-label={`${needsYou} need you`}>
      {needsYou}
    </span>
  ) : null;
}

/** The attention inbox (M5 slice 3, design in docs/design/m5-inbox): everything that needs the user, by task. */
export function Inbox({ tasks, onOpenTask, onOpenThread, onShowOutside }: Props) {
  const { items } = useAttention();
  const [filter, setFilter] = useState<Filter>('all');
  const [lingering, setLingering] = useState<Record<string, Lingering>>({});
  const previous = useRef<AttentionItemDto[]>([]);
  const answeredHere = useRef(new Set<string>());
  const now = useNow();

  // A prompt that goes without being answered here was answered elsewhere (another window, the thread, a
  // notification) or dropped by the agent; say so briefly rather than have the row vanish under the pointer.
  useEffect(() => {
    for (const gone of previous.current) {
      if (!gone.needsYou || !gone.requestId || answeredHere.current.has(gone.id) || items.some((i) => i.id === gone.id)) continue;
      linger(gone, 'No longer waiting: answered elsewhere, or the agent moved on');
    }
    previous.current = items;
  }, [items]);

  function linger(item: AttentionItemDto, receipt: string) {
    answeredHere.current.add(item.id);
    const at = previous.current.findIndex((i) => i.id === item.id);
    setLingering((l) => ({ ...l, [item.id]: { item, receipt, at } }));
    window.setTimeout(() => {
      setLingering((l) => {
        const next = { ...l };
        delete next[item.id];
        return next;
      });
    }, LingerMs);
  }

  const shown = [...items];
  for (const { item, at } of Object.values(lingering).sort((a, b) => a.at - b.at)) {
    if (shown.some((i) => i.id === item.id)) continue;
    shown.splice(at < 0 ? shown.length : Math.min(at, shown.length), 0, item);
  }

  const needCount = items.filter((i) => i.needsYou).length;
  const visible = shown.filter((i) => filter === 'all' || (filter === 'need') === i.needsYou);
  const groups: { taskId: string; items: AttentionItemDto[] }[] = [];
  for (const item of visible) {
    const last = groups[groups.length - 1];
    if (last && last.taskId === item.taskId) last.items.push(item);
    else groups.push({ taskId: item.taskId, items: [item] });
  }

  const filters: { key: Filter; label: string; count: number; cls: string }[] = [
    { key: 'all', label: 'All', count: items.length, cls: '' },
    { key: 'need', label: 'Needs you', count: needCount, cls: 'inbox-filter-need' },
    { key: 'done', label: 'Finished', count: items.length - needCount, cls: 'inbox-filter-done' },
  ];

  return (
    <div className="page inbox">
      <header className="page-header">
        <div className="grow">
          <h1>Inbox</h1>
          <div className="muted">
            What your agents are waiting on, by task. <kbd>Ctrl</kbd> <kbd>.</kbd> goes to the next one from any window.
          </div>
        </div>
        <div className="inbox-filters" role="group" aria-label="Show">
          {filters.map((f) => (
            <button key={f.key} type="button" className={`inbox-filter ${f.cls}${filter === f.key ? ' on' : ''}`} aria-pressed={filter === f.key} onClick={() => setFilter(f.key)}>
              {f.key !== 'all' && <span className="dot" />}
              {f.label} {f.count}
            </button>
          ))}
        </div>
      </header>

      {visible.length === 0 && (
        <div className="inbox-empty">
          <strong>{items.length === 0 ? 'Nothing needs you' : 'Nothing here'}</strong>
          <span className="muted">
            {items.length === 0 ? 'Permission prompts, questions, plans and finished turns from your agents show up here, grouped by task.' : 'Nothing matches this filter.'}
          </span>
        </div>
      )}

      {groups.map((g, gi) => {
        const task = tasks.find((t) => t.id === g.taskId);
        return (
          <section key={`${g.taskId}-${gi}`} className="inbox-group" aria-label={task ? `${task.id} ${task.title}` : 'Not on a task'}>
            <div className="inbox-group-head">
              <div className="grow">
                {task ? (
                  <>
                    <button className="title-link" onClick={() => onOpenTask(task.id)}>
                      {task.title}
                    </button>
                    <code className="muted inbox-id">{task.id}</code>
                  </>
                ) : g.taskId ? (
                  <code className="muted">{g.taskId}</code>
                ) : (
                  <>
                    <span className="inbox-group-title">Not on a task</span>
                    <div className="muted small">Claude sessions started outside Ochre</div>
                  </>
                )}
              </div>
              {task && <StatePill state={task.state} />}
              {task && (
                <button className="link" onClick={() => onOpenTask(task.id)}>
                  Open task
                </button>
              )}
            </div>
            {g.items.map((item) => (
              <Item
                key={item.id}
                item={item}
                receipt={lingering[item.id]?.receipt ?? null}
                now={now}
                onOpenThread={() => onOpenThread(item.sessionId, item.taskId)}
                onShowOutside={onShowOutside}
                onDone={(receipt) => linger(item, receipt)}
              />
            ))}
          </section>
        );
      })}
    </div>
  );
}

interface ItemProps {
  item: AttentionItemDto;
  receipt: string | null;
  now: Date;
  onOpenThread: () => void;
  onShowOutside: () => void;
  onDone: (receipt: string) => void;
}

function Item({ item, receipt, now, onOpenThread, onShowOutside, onDone }: ItemProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Every action names this item's session and prompt; the host refuses one that has moved on, and says why here.
  async function act(run: () => Promise<unknown>, done: string | null) {
    setBusy(true);
    setError(null);
    try {
      await run();
      if (done) onDone(done);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const respond = (allow: boolean, done: string, extra: { switchMode?: string | null; answers?: { question: string; labels: string[] }[]; denyMessage?: string } = {}) =>
    act(
      () =>
        request('sessions.respond', {
          id: item.sessionId,
          requestId: item.requestId ?? '',
          allow,
          switchMode: extra.switchMode ?? null,
          answers: extra.answers ?? null,
          denyMessage: extra.denyMessage ?? null,
        }),
      done,
    );

  const dismiss = () => act(() => request('attention.dismiss', { id: item.id }), null);
  const kindClass = item.kind === 'failed' ? 'inbox-failed' : item.needsYou ? 'inbox-need' : 'inbox-done';
  const name = sessionName({ id: item.sessionId, role: item.role, cwd: item.cwd });
  const age = item.needsYou ? waiting(item.since, now) : when(item.since, now);
  const disabled = busy || receipt !== null;

  return (
    <div className={`inbox-item ${kindClass}${receipt ? ' answered' : ''}`}>
      <div className="inbox-who">
        <span className="dot" aria-hidden="true" />
        <span className={item.kind === 'waiting' && !item.role ? 'who mono' : 'who'} title={item.cwd}>
          {name}
        </span>
        <span className="inbox-what">{item.title}</span>
        <span className="inbox-age">{age}</span>
      </div>
      {body()}
      {receipt ? (
        <div className="inbox-receipt">{receipt}</div>
      ) : (
        error && (
          <p className="inbox-error" role="alert">
            {error}
          </p>
        )
      )}
    </div>
  );

  function body() {
    const p = item.prompt;
    switch (item.kind) {
      case 'permission':
        return (
          <>
            {p ? <pre className="inbox-cmd">{commandOf(p)}</pre> : item.detail && <pre className="inbox-cmd">{item.detail}</pre>}
            {!receipt && (
              <div className="inbox-actions">
                <button className="allow" disabled={disabled || !p} onClick={() => void respond(true, 'Allowed')}>
                  Allow
                </button>
                <button className="deny" disabled={disabled || !p} onClick={() => void respond(false, 'Denied')}>
                  Deny
                </button>
                {p?.suggestedMode && (
                  <button disabled={disabled} onClick={() => void respond(true, `Allowed, mode → ${p.suggestedMode}`, { switchMode: p.suggestedMode })}>
                    Allow, switch to {p.suggestedMode}
                  </button>
                )}
                <button className="link" onClick={onOpenThread}>
                  Open thread
                </button>
                <span className="muted small inbox-where">{folderOf(item.cwd)}</span>
              </div>
            )}
          </>
        );
      case 'question':
        return <Question item={item} prompt={p} disabled={disabled} receipt={receipt} onOpenThread={onOpenThread} respond={respond} />;
      case 'plan':
        return <Plan item={item} prompt={p} disabled={disabled} receipt={receipt} onOpenThread={onOpenThread} respond={respond} />;
      case 'elsewhere':
        return (
          <>
            <p className="muted inbox-text">Claude asked through another client (Remote Control). Open the thread to see where.</p>
            <div className="inbox-actions">
              <button onClick={onOpenThread}>Open thread</button>
              <button className="link" disabled={disabled} onClick={() => void dismiss()}>
                Dismiss
              </button>
            </div>
          </>
        );
      case 'waiting':
        return (
          <>
            <p className="muted inbox-text">Answer it where it runs; it's read-only here.</p>
            <div className="inbox-actions">
              <button onClick={onOpenThread}>Open thread</button>
              {!item.taskId && (
                <button className="link" onClick={onShowOutside}>
                  Link to a task
                </button>
              )}
              <button className="link" disabled={disabled} onClick={() => void dismiss()}>
                Dismiss
              </button>
            </div>
          </>
        );
      case 'finished':
        return <Finished item={item} disabled={disabled} onOpenThread={onOpenThread} onDismiss={dismiss} act={act} />;
      case 'failed':
        return (
          <>
            {item.detail && <p className="muted inbox-text">{item.detail}</p>}
            <div className="inbox-actions">
              <button onClick={onOpenThread}>Open thread</button>
              <button className="link" disabled={disabled} onClick={() => void dismiss()}>
                Dismiss
              </button>
            </div>
          </>
        );
    }
  }
}

type Respond = (allow: boolean, done: string, extra?: { switchMode?: string | null; answers?: { question: string; labels: string[] }[]; denyMessage?: string }) => Promise<void>;

interface PromptProps {
  item: AttentionItemDto;
  prompt: PermissionDto | null;
  disabled: boolean;
  receipt: string | null;
  onOpenThread: () => void;
  respond: Respond;
}

function Question({ item, prompt, disabled, receipt, onOpenThread, respond }: PromptProps) {
  const [chosen, setChosen] = useState<string[]>([]);
  const q = prompt?.questions?.length === 1 ? prompt.questions[0] : null;
  if (!q || q.options.length > MaxInlineOptions) {
    return (
      <>
        {item.detail && <div className="inbox-question">{item.detail}</div>}
        {!receipt && (
          <div className="inbox-actions">
            <button onClick={onOpenThread}>Answer in the thread</button>
          </div>
        )}
      </>
    );
  }
  return (
    <>
      <div className="inbox-question">{q.question}</div>
      {!receipt && (
        <>
          <div className="inbox-options" role={q.multiSelect ? 'group' : 'radiogroup'} aria-label={q.header || q.question}>
            {q.options.map((o) => {
              const on = chosen.includes(o.label);
              return (
                <label key={o.label} className={on ? 'inbox-option on' : 'inbox-option'}>
                  <input
                    type={q.multiSelect ? 'checkbox' : 'radio'}
                    name={item.id}
                    checked={on}
                    onChange={(e) => setChosen((c) => (q.multiSelect ? (e.target.checked ? [...c, o.label] : c.filter((l) => l !== o.label)) : [o.label]))}
                  />
                  <span>
                    {o.label}
                    {o.description && <small>{o.description}</small>}
                  </span>
                </label>
              );
            })}
          </div>
          <div className="inbox-actions">
            <button className="primary" disabled={disabled || chosen.length === 0} onClick={() => void respond(true, `Answered: ${chosen.join(', ')}`, { answers: [{ question: q.question, labels: chosen }] })}>
              Answer
            </button>
            <button className="link" onClick={onOpenThread}>
              Open thread
            </button>
          </div>
        </>
      )}
    </>
  );
}

function Plan({ item, prompt, disabled, receipt, onOpenThread, respond }: PromptProps) {
  const [whole, setWhole] = useState(false);
  const [feedback, setFeedback] = useState('');
  const plan = prompt?.plan ?? null;
  return (
    <>
      {plan !== null ? (
        <div className={whole ? 'inbox-plan whole' : 'inbox-plan'}>
          <div className="markdown" dangerouslySetInnerHTML={{ __html: renderMarkdown(plan) }} />
          {!whole && (
            <button className="link inbox-more" onClick={() => setWhole(true)}>
              Show the whole plan
            </button>
          )}
        </div>
      ) : (
        item.detail && <div className="inbox-question">{item.detail}</div>
      )}
      {!receipt && (
        <div className="inbox-actions">
          <button className="allow" disabled={disabled || !prompt} onClick={() => void respond(true, 'Approved')}>
            Approve
          </button>
          <input
            className="inbox-field"
            type="text"
            value={feedback}
            placeholder="What to change (to reject)"
            aria-label="Feedback on the plan"
            onChange={(e) => setFeedback(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && feedback.trim() && prompt) void respond(false, 'Sent back with your feedback', { denyMessage: feedback.trim() });
            }}
          />
          <button className="deny" disabled={disabled || !prompt} onClick={() => void respond(false, feedback.trim() ? 'Sent back with your feedback' : 'Rejected', { denyMessage: feedback.trim() || undefined })}>
            Reject
          </button>
          <button className="link" onClick={onOpenThread}>
            Open thread
          </button>
        </div>
      )}
    </>
  );
}

interface FinishedProps {
  item: AttentionItemDto;
  disabled: boolean;
  onOpenThread: () => void;
  onDismiss: () => Promise<void>;
  act: (run: () => Promise<unknown>, done: string | null) => Promise<void>;
}

function Finished({ item, disabled, onOpenThread, onDismiss, act }: FinishedProps) {
  const [reply, setReply] = useState('');
  const name = sessionName({ id: item.sessionId, role: item.role, cwd: item.cwd });
  const send = () => {
    const text = reply.trim();
    if (text) void act(() => request('sessions.send', { id: item.sessionId, text }), 'Sent');
  };
  return (
    <>
      {item.detail && <div className="inbox-text">{item.detail}</div>}
      <div className="inbox-actions">
        <input
          className="inbox-field"
          type="text"
          value={reply}
          placeholder={`Reply to ${name}`}
          aria-label={`Reply to ${name}`}
          onChange={(e) => setReply(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') send();
          }}
        />
        <button disabled={disabled || !reply.trim()} onClick={send}>
          Send
        </button>
        <button className="link" onClick={onOpenThread}>
          Open thread
        </button>
        <button className="link" disabled={disabled} onClick={() => void onDismiss()}>
          Dismiss
        </button>
      </div>
    </>
  );
}

// What a permission would run, as the user would read it: the command, the file, else the input itself.
function commandOf(p: PermissionDto): string {
  try {
    const input = JSON.parse(p.inputJson) as Record<string, unknown>;
    for (const key of ['command', 'file_path', 'path', 'url', 'pattern']) {
      const value = input[key];
      if (typeof value === 'string') return value;
    }
    return JSON.stringify(input, null, 2);
  } catch {
    return p.inputJson;
  }
}

const folderOf = (cwd: string) => cwd.split(/[\\/]/).filter(Boolean).pop() ?? cwd;

function waiting(iso: string, now: Date): string {
  const mins = Math.floor((now.getTime() - new Date(iso).getTime()) / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `waiting ${mins} min`;
  const hours = Math.floor(mins / 60);
  return `waiting ${hours} h`;
}

// Re-renders every half minute so the ages move.
function useNow(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(t);
  }, []);
  return now;
}
