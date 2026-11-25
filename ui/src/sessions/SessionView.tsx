import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { on, request, type AssistantPartDto, type PermissionDto, type SessionHeader, type SessionItemDto } from '../bridge';
import { ConfirmDialog } from '../Dialog';
import { renderMarkdown } from '../markdown';
import { PathText } from '../PathText';

interface Props {
  id: string;
  onBack: () => void;
  onError: (message: string) => void;
}

/** The thread of one controlled session: messages, tool cards, prompts that need an answer, and a composer. */
export function SessionView({ id, onBack, onError }: Props) {
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
          ← Back to task
        </button>
        <p className="empty">This agent isn't running in this Ochre any more. Resuming past sessions comes with parking (M2).</p>
      </div>
    );
  }
  if (!header) return <p className="empty">Loading...</p>;

  const ended = header.status === 'ended';

  return (
    <div className="session">
      <header className="session-header">
        <button className="link back" onClick={onBack}>
          ← {header.taskId}
        </button>
        <span className="session-title">{header.role ?? 'Agent'}</span>
        <span className={`status status-${header.status}`}>{header.status}</span>
        <span className="spacer" />
        {!ended && turnActive && <button onClick={() => request('sessions.interrupt', { id }).catch((e: Error) => onError(e.message))}>Interrupt</button>}
        {!ended && (
          <button className="link danger" onClick={() => setConfirmStop(true)}>
            Stop
          </button>
        )}
      </header>
      <div className="session-meta">
        <PathText path={header.cwd} />
        {header.model && <span>{header.model}</span>}
        <span>{header.permissionMode}</span>
        {header.costUsd > 0 && <span>${header.costUsd.toFixed(2)}</span>}
        {header.contextTokens > 0 && <span>{Math.round(header.contextTokens / 1000)}k context</span>}
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
        {turnActive && <p className="working">working...</p>}
      </div>

      {!ended && <Composer sessionId={id} queued={header.queuedPrompts} onError={onError} />}

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
        <p>The Claude session ends. Its conversation is kept by Claude Code and stays listed on the task.</p>
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

function Composer({ sessionId, queued, onError }: { sessionId: string; queued: number; onError: (m: string) => void }) {
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
        value={text}
        placeholder="Message the agent. Enter sends, Shift+Enter adds a line."
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
          Send
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
