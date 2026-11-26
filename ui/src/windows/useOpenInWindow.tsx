import { useCallback, useState } from 'react';
import { request, type OpenTaskChoiceDto, type TaskListItem } from '../bridge';
import { Dialog } from '../Dialog';

/**
 * Opens a task in a window of its own on this desktop. When the only window showing it is on another virtual
 * desktop, asks whether to bring that window here or open another one here; Ochre never switches desktops itself.
 * Returns the opener and the dialog to render.
 */
export function useOpenInWindow(tasks: TaskListItem[], onError: (message: string) => void) {
  const [elsewhere, setElsewhere] = useState<string | null>(null);

  const open = useCallback(
    (id: string, choice: OpenTaskChoiceDto = 'ask') => {
      request('windows.openTask', { id, choice })
        .then((r) => {
          if (r.outcome === 'elsewhere') setElsewhere(id);
        })
        .catch((e: Error) => onError(e.message));
    },
    [onError],
  );

  const pick = (choice: OpenTaskChoiceDto) => {
    const id = elsewhere;
    setElsewhere(null);
    if (id) open(id, choice);
  };

  const title = tasks.find((t) => t.id === elsewhere)?.title;
  const dialog = (
    <Dialog
      open={elsewhere !== null}
      title={`${elsewhere ?? ''} is open on another desktop`}
      onClose={() => setElsewhere(null)}
      onSubmit={() => pick('bring')}
      actions={
        <>
          <button type="button" onClick={() => setElsewhere(null)}>
            Cancel
          </button>
          <button type="button" onClick={() => pick('new')}>
            Open another window here
          </button>
          <button type="submit" className="primary" autoFocus>
            Bring that window here
          </button>
        </>
      }
    >
      <p>
        {title ? <strong>{title}</strong> : 'This task'} is already open in a window on another virtual desktop. Bring that window to this desktop, or open a second window for it here?
      </p>
    </Dialog>
  );

  return { openInWindow: useCallback((id: string) => open(id), [open]), dialog };
}
