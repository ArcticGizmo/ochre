import { useEffect, useRef, type ReactNode } from 'react';

interface DialogProps {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  /** The buttons row. Put the primary action last. */
  actions: ReactNode;
  onSubmit?: () => void;
}

/**
 * A modal on the native <dialog> element, which gives focus trapping, Esc to close and an inert background for
 * free. Used for creates and terminal actions (docs/ux-conventions.md).
 */
export function Dialog({ open, title, onClose, children, actions, onSubmit }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (open && !el.open) el.showModal();
    if (!open && el.open) el.close();
  }, [open]);

  return (
    <dialog ref={ref} className="dialog" onClose={onClose} onCancel={(e) => (e.preventDefault(), onClose())}>
      <form
        method="dialog"
        onSubmit={(e) => {
          e.preventDefault();
          onSubmit?.();
        }}
      >
        <h2>{title}</h2>
        <div className="dialog-body">{children}</div>
        <div className="dialog-actions">{actions}</div>
      </form>
    </dialog>
  );
}

interface ConfirmProps {
  open: boolean;
  title: string;
  confirmLabel: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onClose: () => void;
  children: ReactNode;
}

export function ConfirmDialog({ open, title, confirmLabel, danger, busy, onConfirm, onClose, children }: ConfirmProps) {
  return (
    <Dialog
      open={open}
      title={title}
      onClose={onClose}
      onSubmit={onConfirm}
      actions={
        <>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className={danger ? 'danger' : 'primary'} disabled={busy} autoFocus>
            {confirmLabel}
          </button>
        </>
      }
    >
      {children}
    </Dialog>
  );
}
