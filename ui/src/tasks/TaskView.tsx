import { useCallback, useEffect, useState } from 'react';
import { on, request, type PlanItemDto, type TaskDetail, type TaskStateDto } from '../bridge';
import { ConfirmDialog } from '../Dialog';
import { renderMarkdown } from '../markdown';
import { Menu } from '../Menu';
import { PathText } from '../PathText';
import { SessionView } from '../sessions/SessionView';
import { StartAgentDialog } from '../sessions/StartAgentDialog';
import { useSessions } from '../sessions/useSessions';
import { ParkDialog } from './ParkDialog';
import { LinkSuggestions } from '../sessions/OutsideSessions';
import { agentStatus, agentWord, ParkNote, sessionName, stateLabel, states, when } from './present';

interface Props {
  id: string;
  /** The agent whose thread this window shows in place of the task; per window, so two windows can differ. */
  openSession: string | null;
  onOpenSession: (sessionId: string | null) => void;
  onError: (message: string) => void;
  onDeleted: () => void;
}

export function TaskView({ id, openSession, onOpenSession, onError, onDeleted }: Props) {
  const [task, setTask] = useState<TaskDetail | null>(null);
  const [gone, setGone] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [startingAgent, setStartingAgent] = useState(false);
  const [parking, setParking] = useState(false);
  const [confirmDone, setConfirmDone] = useState(false);
  const live = useSessions();

  const load = useCallback(() => {
    request('tasks.get', { id })
      .then((t) => {
        setTask(t);
        setGone(false);
      })
      .catch((e: Error) => {
        // Deleted elsewhere (another window, or the file removed on disk) isn't an error worth a banner.
        if (e.message === 'no such task') setGone(true);
        else onError(e.message);
      });
  }, [id, onError]);

  useEffect(() => {
    load();
    return on('tasks.changed', (e) => {
      if (e.id === id) load();
    });
  }, [id, load]);

  async function run(p: Promise<TaskDetail>) {
    try {
      setTask(await p);
    } catch (e) {
      onError((e as Error).message);
    }
  }

  async function remove() {
    setDeleting(true);
    try {
      await request('tasks.delete', { id });
      setConfirmDelete(false);
      onDeleted();
    } catch (e) {
      onError((e as Error).message);
    } finally {
      setDeleting(false);
    }
  }

  if (gone) return <p className="empty">This task no longer exists.</p>;
  if (!task) return <p className="empty">Loading...</p>;
  if (openSession)
    return <SessionView key={openSession} id={openSession} backLabel={task.id} parkNote={task.park?.note ?? null} onBack={() => onOpenSession(null)} onError={onError} />;

  const update = (fields: { title?: string; notes?: string; repos?: string[] }) =>
    run(request('tasks.update', { id, title: fields.title ?? null, notes: fields.notes ?? null, repos: fields.repos ?? null }));

  function chooseState(s: TaskStateDto) {
    if (!task || s === task.state) return;
    // Parking and finishing go through their dialogs, so agents are still asked for notes and Done is confirmed.
    if (s === 'parked') setParking(true);
    else if (s === 'done') setConfirmDone(true);
    else void run(request('tasks.setState', { id, state: s }));
  }

  return (
    <article className="task">
      <header className="task-header">
        <div className="task-heading">
          <TitleEditor title={task.title} onSave={(title) => update({ title })} />
          <div className="task-id">
            <code>{task.id}</code>
            {task.created && <span> · created {when(task.created)}</span>}
            {task.updated && <span> · updated {when(task.updated)}</span>}
          </div>
        </div>
        <div className="task-actions">
          <Menu
            ariaLabel={`State: ${stateLabel[task.state]}`}
            buttonClass={`pill state-${task.state} state-button`}
            label={
              <>
                <span className="dot" />
                {stateLabel[task.state]}
                <span className="chevron" aria-hidden="true" />
              </>
            }
            items={states.map((s) => ({
              key: s,
              label: (
                <>
                  <span className={`dot state-${s}`} />
                  <span className="grow">{s === 'done' && task.state !== 'done' ? 'Done…' : s === 'parked' && task.state !== 'parked' ? 'Parked…' : stateLabel[s]}</span>
                </>
              ),
              current: s === task.state,
              separatorBefore: s === 'done',
              onSelect: () => chooseState(s),
            }))}
          />
          {task.state === 'parked' ? (
            <button className="primary" onClick={() => run(request('tasks.setState', { id, state: 'active' }))}>
              Resume
            </button>
          ) : (
            task.state !== 'done' && <button onClick={() => setParking(true)}>Park</button>
          )}
          <Menu
            ariaLabel="More actions"
            buttonClass="link more"
            label={<span aria-hidden="true">⋯</span>}
            items={[{ key: 'delete', label: 'Delete task…', danger: true, onSelect: () => setConfirmDelete(true) }]}
          />
        </div>
      </header>

      <ConfirmDialog
        open={confirmDone}
        title={`Mark ${task.id} done?`}
        confirmLabel="Mark done"
        onConfirm={() => {
          setConfirmDone(false);
          void run(request('tasks.setState', { id, state: 'done' }));
        }}
        onClose={() => setConfirmDone(false)}
      >
        <p>
          <strong>{task.title}</strong> moves to Done and leaves Today and the open list. Its agents aren't stopped. You can reopen it from the Done filter.
        </p>
      </ConfirmDialog>

      <ParkDialog
        open={parking}
        taskId={task.id}
        title={task.title}
        running={live.filter((s) => s.taskId === task.id && s.status !== 'dormant' && !s.observed)}
        onClose={() => setParking(false)}
        onParked={() => setParking(false)}
      />

      <ConfirmDialog
        open={confirmDelete}
        title={`Delete ${task.id}?`}
        confirmLabel="Delete task"
        danger
        busy={deleting}
        onConfirm={remove}
        onClose={() => setConfirmDelete(false)}
      >
        <p>
          <strong>{task.title}</strong> will be removed from Ochre. Its file moves to the <code>.trash</code> folder next to your tasks, so it can be restored by moving it back. Its id won't be reused.
        </p>
      </ConfirmDialog>

      {task.park && (task.park.note || task.parkingWaitingFor.length > 0) && (task.state === 'parked' || task.state === 'active') && (
        <section className="park">
          <h3>
            Where you left off <span className="h3-aside">· parked {when(task.park.at)}</span>
          </h3>
          {task.park.note && <ParkNote note={task.park.note} />}
          {task.parkingWaitingFor.length > 0 && (
            <p className="muted">
              Waiting for park notes from{' '}
              {task.parkingWaitingFor.map((sid) => task.sessions.find((s) => s.id === sid)?.role ?? sid.slice(0, 8)).join(', ')}...
            </p>
          )}
        </section>
      )}

      <LinkSuggestions taskId={task.id} live={live} onOpen={onOpenSession} onError={onError} />

      {(task.agentChangedItems.length > 0 || task.agentChangedNotes) && (
        <div className="review-bar">
          <span>
            Agents changed{' '}
            {[
              task.agentChangedItems.length > 0 && `${task.agentChangedItems.length} plan item${task.agentChangedItems.length === 1 ? '' : 's'}`,
              task.agentChangedNotes && 'the notes',
            ]
              .filter(Boolean)
              .join(' and ')}{' '}
            since you last looked.
          </span>
          <button onClick={() => run(request('tasks.reviewAgentChanges', { id }))}>Mark reviewed</button>
        </div>
      )}

      <section>
        <h3>Plan</h3>
        {task.planText && <div className="markdown plan-text" dangerouslySetInnerHTML={{ __html: renderMarkdown(task.planText) }} />}
        <Plan
          items={task.plan}
          agentChanged={task.agentChangedItems}
          onCheck={(itemId, done) => run(request('plan.check', { id, itemId, done }))}
          onAdd={(text, parentId) => run(request('plan.add', { id, text, parentId }))}
        />
      </section>

      <section className={task.agentChangedNotes ? 'agent-changed-notes' : undefined}>
        <h3>Notes{task.agentChangedNotes && <span className="agent-tag">agent</span>}</h3>
        <Notes notes={task.notes} onSave={(notes) => update({ notes })} />
      </section>

      <section>
        <h3>Repos</h3>
        <Repos repos={task.repos} onSave={(repos) => update({ repos })} />
      </section>

      <section>
        <h3>Agents</h3>
        {task.sessions.length === 0 ? (
          <p className="empty">No agents yet.</p>
        ) : (
          <ul className="plain agents">
            {task.sessions.map((s) => {
              const status = agentStatus(s.id, live);
              return (
                <li key={s.id}>
                  <span className={`status status-${status}`}>
                    <span className="dot" />
                    {agentWord(status)}
                  </span>
                  <span className="who">{sessionName(s)}</span>
                  {s.binding === 'confirmed' && <span className="tag-outside">outside Ochre</span>}
                  <PathText path={s.cwd} />
                  <code title={s.id}>{s.id.slice(0, 8)}</code>
                  {status !== 'ended' && (
                    <button className="link" onClick={() => onOpenSession(s.id)}>
                      Open
                    </button>
                  )}
                  {s.binding === 'confirmed' && (
                    <button className="link" title="Take this session off the task; it carries on outside Ochre" onClick={() => request('sessions.unbind', { id: s.id }).catch((e: Error) => onError(e.message))}>
                      Unlink
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
        <button className="start-agent" onClick={() => setStartingAgent(true)}>
          Start agent
        </button>
        <StartAgentDialog
          open={startingAgent}
          taskId={task.id}
          repos={task.repos}
          onClose={() => setStartingAgent(false)}
          onStarted={(sessionId) => {
            setStartingAgent(false);
            onOpenSession(sessionId);
          }}
        />
      </section>

      {task.links.length > 0 && (
        <section>
          <h3>Links</h3>
          <ul className="plain">
            {task.links.map((l) => (
              <li key={l.id}>
                <span className="kind">{l.kind}</span> {l.ref}
                {l.title && <> - {l.title}</>}
              </li>
            ))}
          </ul>
        </section>
      )}

      <section>
        <details>
          <summary>Log ({task.log.length})</summary>
          <ul className="log">
            {[...task.log].reverse().map((line, i) => (
              <li key={i}>{line}</li>
            ))}
          </ul>
        </details>
      </section>
    </article>
  );
}

function TitleEditor({ title, onSave }: { title: string; onSave: (title: string) => void }) {
  const [value, setValue] = useState(title);
  useEffect(() => setValue(title), [title]);
  const commit = () => {
    const t = value.trim();
    if (t && t !== title) onSave(t);
    else setValue(title);
  };
  return (
    <input
      className="title-input"
      value={value}
      maxLength={200}
      onChange={(e) => setValue(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') e.currentTarget.blur();
        if (e.key === 'Escape') {
          setValue(title);
          e.currentTarget.blur();
        }
      }}
    />
  );
}

interface PlanProps {
  items: PlanItemDto[];
  /** Items agents added or ticked since the user last reviewed them. */
  agentChanged: string[];
  onCheck: (itemId: string, done: boolean) => void;
  onAdd: (text: string, parentId: string | null) => void;
}

function Plan({ items, agentChanged, onCheck, onAdd }: PlanProps) {
  const [addingUnder, setAddingUnder] = useState<string | null>(null);

  const renderItems = (list: PlanItemDto[]) => (
    <ul className="plan">
      {list.map((item) => (
        <li key={item.id}>
          <label className={[item.done ? 'done' : '', agentChanged.includes(item.id) ? 'agent-changed' : ''].join(' ')}>
            <input type="checkbox" checked={item.done} onChange={(e) => onCheck(item.id, e.target.checked)} />
            <span>{item.text}</span>
            {agentChanged.includes(item.id) && <span className="agent-tag">agent</span>}
          </label>
          <button className="link add-sub" title="Add a sub-step" onClick={() => setAddingUnder(item.id)}>
            +
          </button>
          {item.children.length > 0 && renderItems(item.children)}
          {addingUnder === item.id && (
            <StepInput
              autoFocus
              placeholder="Sub-step"
              onSubmit={(text) => {
                onAdd(text, item.id);
                setAddingUnder(null);
              }}
              onCancel={() => setAddingUnder(null)}
            />
          )}
        </li>
      ))}
    </ul>
  );

  return (
    <>
      {items.length > 0 && renderItems(items)}
      <StepInput placeholder="Add a step" onSubmit={(text) => onAdd(text, null)} />
    </>
  );
}

function StepInput({ placeholder, autoFocus, onSubmit, onCancel }: { placeholder: string; autoFocus?: boolean; onSubmit: (text: string) => void; onCancel?: () => void }) {
  const [text, setText] = useState('');
  return (
    <form
      className="step-input"
      onSubmit={(e) => {
        e.preventDefault();
        const t = text.trim();
        if (!t) return;
        onSubmit(t);
        setText('');
      }}
    >
      <input
        autoFocus={autoFocus}
        value={text}
        maxLength={500}
        placeholder={placeholder}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => e.key === 'Escape' && onCancel?.()}
        onBlur={() => !text && onCancel?.()}
      />
    </form>
  );
}

function Notes({ notes, onSave }: { notes: string; onSave: (notes: string) => void }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(notes);

  if (editing) {
    return (
      <div className="notes-edit">
        <textarea value={draft} onChange={(e) => setDraft(e.target.value)} rows={10} autoFocus />
        <div className="actions">
          <button
            onClick={() => {
              onSave(draft);
              setEditing(false);
            }}
          >
            Save
          </button>
          <button className="link" onClick={() => setEditing(false)}>
            Cancel
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="notes">
      {notes ? <div className="markdown" dangerouslySetInnerHTML={{ __html: renderMarkdown(notes) }} /> : <p className="empty">No notes.</p>}
      <button
        className="link"
        onClick={() => {
          setDraft(notes);
          setEditing(true);
        }}
      >
        Edit notes
      </button>
    </div>
  );
}

function Repos({ repos, onSave }: { repos: string[]; onSave: (repos: string[]) => void }) {
  const [path, setPath] = useState('');
  return (
    <div className="repos">
      {repos.length === 0 && <p className="empty">No repos. Agents need one to start in.</p>}
      <ul className="plain">
        {repos.map((r) => (
          <li key={r}>
            <PathText path={r} />
            <button className="link" onClick={() => onSave(repos.filter((x) => x !== r))}>
              Remove
            </button>
          </li>
        ))}
      </ul>
      <form
        className="step-input"
        onSubmit={(e) => {
          e.preventDefault();
          const p = path.trim();
          if (!p) return;
          onSave([...repos, p]);
          setPath('');
        }}
      >
        <input value={path} onChange={(e) => setPath(e.target.value)} placeholder="C:\path\to\repo" />
      </form>
    </div>
  );
}
