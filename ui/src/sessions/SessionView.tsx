import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { on, request, type AssistantPartDto, type PermissionDto, type SessionHeader, type SessionItemDto } from '../bridge';
import { ConfirmDialog } from '../Dialog';
import { renderMarkdown } from '../markdown';
import { PathText } from '../PathText';
import { Menu } from '../Menu';
import { agentWord, parkNoteLines, sessionName, statusOf } from '../tasks/present';

// The latest of each family. The host accepts any model id, so this list is only what's offered.
const models = [
  { id: 'claude-fable-5-2', label: 'Fable 5.2' },
  { id: 'claude-opus-5-5', label: 'Opus 5.5' },
  { id: 'claude-sonnet-5-5', label: 'Sonnet 5.5' },
  { id: 'claude-haiku-5-5', label: 'Haiku 5.5' },
];

const efforts = ['auto', 'low', 'medium', 'high', 'xhigh', 'max'];

const modelLabel = (id: string) => models.find((m) => m.id === id)?.label ?? (id || 'Default model');

interface Props {
  id: string;
  /** Where the back button returns to, e.g. the task id or "Today". */
  backLabel: string;
  /** The task's park note; this agent's line from it is shown above the composer while it's dormant. */
  parkNote: string | null;
  onBack: () => void;
  onError: (message: string) => void;
}

/** The thread of one controlled session: messages, tool cards, prompts that need an answer, and a composer. */
export function SessionView({ id, backLabel, parkNote, onBack, onError }: Props) {
  const [header, setHeader] = useState<SessionHeader | null>(null);
  const [items, setItems] = useState<SessionItemDto[]>([]);
  const [turnActive, setTurnActive] = useState(false);
  const [gone, setGone] = useState(false);
  const [confirmStop, setConfirmStop] = useState(false);
  const scroller = useRef<HTMLDivElement>(null);
  const stick = useRef(true);

  const load = useCallback(() => {
    request('sessions.get', { id })
      .then((s) => {
        setHeader(s.header);
        setItems(s.items);
        setTurnActive(s.turnActive);
      })
      .catch((e: Error) => (e.message === 'no such session' ? setGone(true) : onError(e.message)));
  }, [id, onError]);

  useEffect(() => {
    load();
    return on('sessions.update', (e) => {
      if (e.id !== id) return;
      if (e.reset) {
        load();
        return;
      }
      setHeader(e.header);
      setTurnActive(e.turnActive);
      if (e.items.length === 0) return;
      setItems((list) => {
        const next = [...list];
        for (const item of e.items) next[item.index] = item;
        // An index can only be skipped if an update was missed; refetch rather than show holes.
        if (next.some((x) => x === undefined)) {
          load();
          return list;
        }
        return next;
      });
    });
  }, [id, load]);

  // Follow the thread while the reader is at the bottom; leave them alone once they scroll up.
  useLayoutEffect(() => {
    const el = scroller.current;
    if (el && stick.current) el.scrollTop = el.scrollHeight;
  }, [items, turnActive]);

  if (gone) {
    return (
      <div className="session">
        <button className="link back" onClick={onBack}>
          ← {backLabel}
        </button>
        <p className="empty">Ochre doesn't know this agent. It may belong to a task that was deleted.</p>
      </div>
    );
  }
  if (!header) return <p className="empty">Loading...</p>;

  const observed = header.observed;
  const dormant = header.status === 'dormant';
  const ownNote = dormant && !observed && parkNote ? parkNoteLines(parkNote).find((l) => l.role !== null && l.role === header.role) : undefined;
  const status = statusOf(header);

  return (
    <div className="session">
      <header className="session-header">
        <button className="link back" onClick={onBack}>
          ← {backLabel}
        </button>
        <span className="session-title">{observed ? sessionName(header) : (header.role ?? 'Agent')}</span>
        <span className={`status status-${status}`}>
          <span className="dot" />
          {agentWord(status)}
        </span>
        {observed && <span className="tag-outside">outside Ochre</span>}
        <span className="spacer" />
        {!observed && (
          <>
            <Menu
              ariaLabel={`Model: ${modelLabel(header.model)}`}
              buttonClass="picker"
              label={
                <>
                  {modelLabel(header.model)}
                  <span className="chevron" aria-hidden="true" />
                </>
              }
              items={models.map((m) => ({
                key: m.id,
                label: <span className="grow">{m.label}</span>,
                current: m.id === header.model,
                onSelect: () => request('sessions.setModel', { id, model: m.id }).catch((e: Error) => onError(e.message)),
              }))}
            />
            <Menu
              ariaLabel={`Effort: ${header.effort}`}
              buttonClass="picker"
              label={
                <>
                  effort {header.effort}
                  <span className="chevron" aria-hidden="true" />
                </>
              }
              items={efforts.map((level) => ({
                key: level,
                label: <span className="grow">{level}</span>,
                current: level === header.effort,
                onSelect: () => request('sessions.setEffort', { id, effort: level }).catch((e: Error) => onError(e.message)),
              }))}
            />
          </>
        )}
        {!observed && !dormant && turnActive && <button onClick={() => request('sessions.interrupt', { id }).catch((e: Error) => onError(e.message))}>Interrupt</button>}
        {!observed && !dormant && (
          <button className="link danger" onClick={() => setConfirmStop(true)}>
            Stop
          </button>
        )}
      </header>
      <div className="session-meta">
        <PathText path={header.cwd} />
        {header.branch && <span>{header.branch}</span>}
        {!observed && <span>{header.permissionMode} mode</span>}
        {header.contextTokens > 0 && (
          <span title={header.costUsd > 0 ? `≈ $${header.costUsd.toFixed(2)} at API prices. On a Pro or Max plan nothing is billed per token.` : undefined}>
            {Math.round(header.contextTokens / 1000)}k context
          </span>
        )}
        <code title={header.id}>{header.id.slice(0, 8)}</code>
      </div>

      <div
        className="thread"
        ref={scroller}
        onScroll={(e) => {
          const el = e.currentTarget;
          stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
        }}
      >
        {items.length === 0 && <p className="empty">Nothing yet. Write the first message below.</p>}
        {items.map((item) => (
          <Item key={item.index} sessionId={id} item={item} onError={onError} />
        ))}
        {turnActive && <div className="msg assistant working">working…</div>}
      </div>

      {ownNote && (
        <p className="own-note muted">
          <strong className="who">{ownNote.role}'s park note:</strong> {ownNote.text}
        </p>
      )}
      {observed ? (
        <p className="read-only-note muted">
          Started outside Ochre, so this is read-only: Ochre follows its transcript. Carry on with it where it's running{dormant ? ' (it has closed; resume it there with claude --resume)' : ''}.
        </p>
      ) : (
        <Composer sessionId={id} queued={header.queuedPrompts} dormant={dormant} autoFocus={dormant || header.status === 'idle'} onError={onError} />
      )}

      <ConfirmDialog
        open={confirmStop}
        title="Stop this agent?"
        confirmLabel="Stop agent"
        danger
        onConfirm={() => {
          setConfirmStop(false);
          request('sessions.stop', { id }).catch((e: Error) => onError(e.message));
        }}
        onClose={() => setConfirmStop(false)}
      >
        <p>The Claude session ends and goes dormant. Its conversation stays on the task; sending a message resumes it.</p>
      </ConfirmDialog>
    </div>
  );
}

function Item({ sessionId, item, onError }: { sessionId: string; item: SessionItemDto; onError: (m: string) => void }) {
  switch (item.kind) {
    case 'user':
      return <div className="msg user">{item.text}</div>;
    case 'note':
      return <p className={item.error ? 'note error' : 'note'}>{item.text}</p>;
    case 'assistant':
      return (
        <div className="msg assistant">
          {(item.parts ?? []).map((p, i) => (
            <Part key={i} part={p} />
          ))}
        </div>
      );
    case 'permission':
      return item.permission ? <Permission sessionId={sessionId} p={item.permission} onError={onError} /> : null;
  }
}

function Part({ part }: { part: AssistantPartDto }) {
  if (part.kind === 'text') {
    // Streaming text stays plain so a half-written code fence doesn't reflow the whole reply on every batch.
    return part.streaming ? <div className="text streaming">{part.text}</div> : <div className="text markdown" dangerouslySetInnerHTML={{ __html: renderMarkdown(part.text) }} />;
  }
  if (part.kind === 'thinking') {
    return (
      <details className="thinking">
        <summary>Thinking</summary>
        <div className="text">{part.text}</div>
      </details>
    );
  }
  return (
    <details className={`tool tool-${part.status}`}>
      <summary>
        <span className="tool-status" aria-label={part.status ?? ''} />
        {part.text}
      </summary>
      {part.diff ? (
        <pre className="diff">
          {part.diff.map((line, i) => (
            <span key={i} className={line[0] === '+' ? 'add' : line[0] === '-' ? 'del' : line[0] === '@' ? 'meta' : ''}>
              {line.slice(1)}
              {'\n'}
            </span>
          ))}
        </pre>
      ) : (
        part.inputJson && part.inputJson !== '{}' && <pre className="tool-input">{pretty(part.inputJson)}</pre>
      )}
      {part.result && <pre className="tool-result">{part.result}</pre>}
    </details>
  );
}

function Permission({ sessionId, p, onError }: { sessionId: string; p: PermissionDto; onError: (m: string) => void }) {
  const [answers, setAnswers] = useState<Record<string, string[]>>({});
  const [busy, setBusy] = useState(false);

  if (p.resolution !== 'pending') {
    const what = p.questions ? 'Question' : p.plan !== null ? 'Plan' : p.toolName;
    const outcome =
      p.resolution === 'allowed'
        ? p.answerSummary ?? (p.switchedMode ? `allowed, mode → ${p.switchedMode}` : 'allowed')
        : p.resolution === 'denied'
          ? 'declined'
          : p.resolution === 'answeredElsewhere'
            ? 'answered elsewhere'
            : 'no longer waiting';
    return (
      <p className="note">
        {what}: {outcome}
      </p>
    );
  }

  async function respond(allow: boolean, switchMode: string | null = null, withAnswers = false) {
    setBusy(true);
    try {
      await request('sessions.respond', {
        id: sessionId,
        requestId: p.requestId,
        allow,
        switchMode,
        answers: withAnswers ? Object.entries(answers).map(([question, labels]) => ({ question, labels })) : null,
      });
    } catch (e) {
      onError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (p.questions) {
    const complete = p.questions.every((q) => (answers[q.question] ?? []).length > 0);
    return (
      <div className="card ask">
        {p.questions.map((q) => (
          <fieldset key={q.question}>
            <legend>{q.question}</legend>
            {q.options.map((o) => {
              const chosen = (answers[q.question] ?? []).includes(o.label);
              return (
                <label key={o.label} className="option">
                  <input
                    type={q.multiSelect ? 'checkbox' : 'radio'}
                    name={q.question}
                    checked={chosen}
                    onChange={(e) =>
                      setAnswers((a) => {
                        const current = a[q.question] ?? [];
                        const next = q.multiSelect ? (e.target.checked ? [...current, o.label] : current.filter((l) => l !== o.label)) : [o.label];
                        return { ...a, [q.question]: next };
                      })
                    }
                  />
                  <span>
                    {o.label}
                    {o.description && <small>{o.description}</small>}
                  </span>
                </label>
              );
            })}
          </fieldset>
        ))}
        <div className="card-actions">
          <button onClick={() => respond(false)} disabled={busy}>
            Decline
          </button>
          <button className="primary" onClick={() => respond(true, null, true)} disabled={busy || !complete}>
            Answer
          </button>
        </div>
      </div>
    );
  }

  if (p.plan !== null) {
    return (
      <div className="card plan-approval">
        <h4>Plan ready</h4>
        <div className="markdown" dangerouslySetInnerHTML={{ __html: renderMarkdown(p.plan) }} />
        <div className="card-actions">
          <button onClick={() => respond(false)} disabled={busy}>
            Keep planning
          </button>
          {p.suggestedMode && (
            <button onClick={() => respond(true, p.suggestedMode)} disabled={busy}>
              Approve, {p.suggestedMode}
            </button>
          )}
          <button className="primary" onClick={() => respond(true)} disabled={busy}>
            Approve
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="card permission">
      <h4>Allow {p.toolName}?</h4>
      {p.description && <p>{p.description}</p>}
      {p.inputJson !== '{}' && <pre className="tool-input">{pretty(p.inputJson)}</pre>}
      <div className="card-actions">
        <button onClick={() => respond(false)} disabled={busy}>
          Deny
        </button>
        {p.suggestedMode && (
          <button onClick={() => respond(true, p.suggestedMode)} disabled={busy}>
            Allow, switch to {p.suggestedMode}
          </button>
        )}
        <button className="primary" onClick={() => respond(true)} disabled={busy} autoFocus>
          Allow
        </button>
      </div>
    </div>
  );
}

function Composer({ sessionId, queued, dormant, autoFocus, onError }: { sessionId: string; queued: number; dormant: boolean; autoFocus: boolean; onError: (m: string) => void }) {
  const [text, setText] = useState('');
  const [sending, setSending] = useState(false);

  async function send() {
    const t = text.trim();
    if (!t || sending) return;
    setSending(true);
    try {
      await request('sessions.send', { id: sessionId, text: t });
      setText('');
    } catch (e) {
      onError((e as Error).message);
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="composer">
      <textarea
        rows={3}
        autoFocus={autoFocus}
        aria-label="Message the agent"
        value={text}
        placeholder={dormant ? 'This agent is dormant. Sending resumes it with its conversation.' : 'Message the agent. Enter sends, Shift+Enter adds a line.'}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            void send();
          }
        }}
      />
      <div className="composer-actions">
        {queued > 0 && <span className="muted">{queued} queued</span>}
        <button className="primary" onClick={() => void send()} disabled={sending || !text.trim()}>
          {dormant ? 'Resume and send' : 'Send'}
        </button>
      </div>
    </div>
  );
}

function pretty(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
