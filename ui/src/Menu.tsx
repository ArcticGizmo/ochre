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

  const focusIndex = Math.max(0, items.findIndex((i) => i.current));

  return (
    <div className="menu" ref={root}>
      <button type="button" className={buttonClass} aria-haspopup="menu" aria-expanded={open} aria-label={ariaLabel} onClick={() => setOpen((o) => !o)}>
        {label}
      </button>
      {open && (
        <div className="menu-list" role="menu">
          {items.map((item, i) => (
            <Fragment key={item.key}>
              {item.separatorBefore && <div className="menu-sep" />}
              <button
                type="button"
                role="menuitem"
                autoFocus={i === focusIndex}
                className={['menu-item', item.current ? 'current' : '', item.danger ? 'danger' : ''].join(' ')}
                onClick={() => {
                  setOpen(false);
                  item.onSelect();
                }}
              >
                {item.label}
                {item.current && <span className="check" aria-label="current" />}
              </button>
            </Fragment>
          ))}
        </div>
      )}
    </div>
  );
}
