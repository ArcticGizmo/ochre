import { useCallback, useEffect, useState } from 'react';
import { on, request, type PlanItemDto, type TaskDetail, type TaskStateDto } from '../bridge';
import { renderMarkdown } from '../markdown';
import { PathText } from '../PathText';

const states: TaskStateDto[] = ['backlog', 'active', 'parked', 'done'];

interface Props {
  id: string;
  onError: (message: string) => void;
}

export function TaskView({ id, onError }: Props) {
  const [task, setTask] = useState<TaskDetail | null>(null);

  const load = useCallback(() => {
    request('tasks.get', { id })
      .then(setTask)
      .catch((e: Error) => onError(e.message));
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

  if (!task) return <p className="empty">Loading...</p>;

  const update = (fields: { title?: string; notes?: string; repos?: string[] }) =>
    run(request('tasks.update', { id, title: fields.title ?? null, notes: fields.notes ?? null, repos: fields.repos ?? null }));

  return (
    <article className="task">
      <header className="task-header">
        <TitleEditor title={task.title} onSave={(title) => update({ title })} />
        <span className="task-id">{task.id}</span>
      </header>

      <div className="states" role="group" aria-label="State">
        {states.map((s) => (
          <button key={s} className={s === task.state ? `chip active state-${s}` : 'chip'} onClick={() => run(request('tasks.setState', { id, state: s }))}>
            {s}
          </button>
        ))}
      </div>

      {task.park && task.state === 'parked' && (
        <section className="park">
          <h3>Where you left off</h3>
          <p>{task.park.note}</p>
        </section>
      )}

      <section>
        <h3>Plan</h3>
        <Plan
          items={task.plan}
          onCheck={(itemId, done) => run(request('plan.check', { id, itemId, done }))}
          onAdd={(text, parentId) => run(request('plan.add', { id, text, parentId }))}
        />
      </section>

      <section>
        <h3>Notes</h3>
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
          <ul className="plain">
            {task.sessions.map((s) => (
              <li key={s.id}>
                <code>{s.id.slice(0, 8)}</code> {s.role ?? 'agent'} in <PathText path={s.cwd} />
              </li>
            ))}
          </ul>
        )}
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
  onCheck: (itemId: string, done: boolean) => void;
  onAdd: (text: string, parentId: string | null) => void;
}

function Plan({ items, onCheck, onAdd }: PlanProps) {
  const [addingUnder, setAddingUnder] = useState<string | null>(null);

  const renderItems = (list: PlanItemDto[]) => (
    <ul className="plan">
      {list.map((item) => (
        <li key={item.id}>
          <label className={item.done ? 'done' : ''}>
            <input type="checkbox" checked={item.done} onChange={(e) => onCheck(item.id, e.target.checked)} />
            <span>{item.text}</span>
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
      {items.length > 0 ? renderItems(items) : <p className="empty">No plan yet.</p>}
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
