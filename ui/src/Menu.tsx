import { Fragment, useEffect, useRef, useState, type ReactNode } from 'react';

export interface MenuItem {
  key: string;
  label: ReactNode;
  onSelect: () => void;
  current?: boolean;
  danger?: boolean;
  separatorBefore?: boolean;
}

interface Props {
  label: ReactNode;
  ariaLabel?: string;
  buttonClass?: string;
  items: MenuItem[];
}

/** A button that drops down a short list of actions. Closes on a pick, a click outside, or Esc. */
export function Menu({ label, ariaLabel, buttonClass, items }: Props) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const outside = (e: MouseEvent) => {
      if (!root.current?.contains(e.target as Node)) setOpen(false);
    };
    const esc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', outside);
    document.addEventListener('keydown', esc);
    return () => {
      document.removeEventListener('mousedown', outside);
      document.removeEventListener('keydown', esc);
    };
  }, [open]);

  return (
    <div className="menu" ref={root}>
      <button type="button" className={buttonClass} aria-haspopup="menu" aria-expanded={open} aria-label={ariaLabel} onClick={() => setOpen((o) => !o)}>
        {label}
      </button>
      {open && (
        <div className="menu-list" role="menu">
          <MenuItems items={items} onPicked={() => setOpen(false)} />
        </div>
      )}
    </div>
  );
}

function MenuItems({ items, onPicked }: { items: MenuItem[]; onPicked: () => void }) {
  const focusIndex = Math.max(0, items.findIndex((i) => i.current));
  return (
    <>
      {items.map((item, i) => (
        <Fragment key={item.key}>
          {item.separatorBefore && <div className="menu-sep" />}
          <button
            type="button"
            role="menuitem"
            autoFocus={i === focusIndex}
            className={['menu-item', item.current ? 'current' : '', item.danger ? 'danger' : ''].join(' ')}
            onClick={() => {
              onPicked();
              item.onSelect();
            }}
          >
            {item.label}
            {item.current && <span className="check" aria-label="current" />}
          </button>
        </Fragment>
      ))}
    </>
  );
}

/** A right-click menu at the pointer. Closes on a pick, a click outside, Esc, scrolling or the window losing focus. */
export function ContextMenu({ at, items, onClose }: { at: { x: number; y: number } | null; items: MenuItem[]; onClose: () => void }) {
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!at) return;
    const outside = (e: MouseEvent) => {
      if (!root.current?.contains(e.target as Node)) onClose();
    };
    const esc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('mousedown', outside);
    document.addEventListener('keydown', esc);
    window.addEventListener('blur', onClose);
    window.addEventListener('scroll', onClose, true);
    return () => {
      document.removeEventListener('mousedown', outside);
      document.removeEventListener('keydown', esc);
      window.removeEventListener('blur', onClose);
      window.removeEventListener('scroll', onClose, true);
    };
  }, [at, onClose]);

  if (!at) return null;
  // Kept inside the window: flip left or up when the pointer is near the right or bottom edge.
  const style = {
    left: Math.min(at.x, window.innerWidth - 220),
    top: Math.min(at.y, window.innerHeight - 8 - 34 * items.length),
  };
  return (
    <div className="menu-list context-menu" role="menu" ref={root} style={style}>
      <MenuItems items={items} onPicked={onClose} />
    </div>
  );
}
