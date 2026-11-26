import { FitAddon } from '@xterm/addon-fit';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { Terminal, type ILinkHandler } from '@xterm/xterm';
import '@xterm/xterm/css/xterm.css';
import { useEffect, useRef } from 'react';
import { on, request, type TerminalOutputEvent } from '../bridge';

interface Props {
  id: string;
  /** The link under the pointer, so the view can show where it goes before it's opened. */
  onLinkHover: (url: string | null) => void;
  onError: (message: string) => void;
  /** Brought back after a restart: open scrolled to the divider, with the old output above it. */
  restored: boolean;
}

// The new shell's screen sits below the divider, so without this a restored tab opens looking empty.
function showRestoreDivider(term: Terminal) {
  const buffer = term.buffer.active;
  for (let y = buffer.length - 1; y >= 0; y--) {
    if (buffer.getLine(y)?.translateToString(true).includes('──── restored')) {
      term.scrollToLine(Math.max(0, y - Math.floor(term.rows / 2)));
      return;
    }
  }
}

/**
 * One terminal drawn with xterm.js. The host owns the shell and its scrollback (ADR 0011); this replays the
 * scrollback, then applies only chunks numbered after it. Loaded lazily so xterm stays off the window-open path.
 */
export default function XtermPane({ id, onLinkHover, onError, restored }: Props) {
  const box = useRef<HTMLDivElement>(null);
  // Kept in refs so a parent re-render doesn't tear the terminal down.
  const hoverRef = useRef(onLinkHover);
  const errorRef = useRef(onError);
  const restoredRef = useRef(restored);
  hoverRef.current = onLinkHover;
  errorRef.current = onError;
  restoredRef.current = restored;

  useEffect(() => {
    const el = box.current;
    if (!el) return;
    let disposed = false;
    // Until the replay has been parsed, anything xterm sends back is its answer to queries in old output.
    let replaying = true;
    let lastSeq = -1;
    const early: TerminalOutputEvent[] = [];
    let hovered: string | null = null;

    const openUrl = (url: string, newWindow: boolean) => request('app.openUrl', { url, newWindow }).catch((e: Error) => errorRef.current(e.message));
    // A plain click selects text as usual; Ctrl+click opens, as in other terminals. Middle click is handled below.
    const activate = (e: MouseEvent, url: string) => {
      if (e.button === 0 && (e.ctrlKey || e.metaKey)) void openUrl(url, false);
    };
    const hover = (_e: MouseEvent, url: string) => {
      hovered = url;
      hoverRef.current(url);
    };
    const leave = () => {
      hovered = null;
      hoverRef.current(null);
    };
    // OSC 8 links; the host has already dropped any whose target isn't http(s).
    const linkHandler: ILinkHandler = { allowNonHttpProtocols: false, activate, hover, leave };

    const term = new Terminal({
      fontFamily: '"Cascadia Mono", Consolas, monospace',
      fontSize: 13,
      scrollback: 10000,
      cursorBlink: true,
      // A new shell under ConPTY starts by clearing the screen; this keeps what was there (a restored tab's old
      // output, or anything before a `cls`) in the scrollback, as Windows Terminal does.
      scrollOnEraseInDisplay: true,
      linkHandler,
      theme: {
        background: '#141312',
        foreground: '#d6d1cb',
        cursor: '#e8974a',
        selectionBackground: 'rgba(232, 151, 74, 0.35)',
      },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    // Plain URLs in the output, such as a dev server's address.
    term.loadAddon(new WebLinksAddon(activate, { hover, leave }));
    term.open(el);
    fit.fit();

    // When the user last typed, pasted or clicked in this terminal; data right after that is theirs.
    let userInputAt = -Infinity;
    const noteUserInput = () => {
      userInputAt = performance.now();
    };

    term.attachCustomKeyEventHandler((e) => {
      if (e.type === 'keydown') noteUserInput();
      if (e.type !== 'keydown' || !e.ctrlKey) return true;
      const key = e.key.toLowerCase();
      // Ctrl+C copies when there's a selection (else it's an interrupt); Ctrl+V is left to the browser's paste.
      if (key === 'c' && term.hasSelection()) {
        void navigator.clipboard.writeText(term.getSelection()).catch(() => {});
        term.clearSelection();
        return false;
      }
      return key !== 'v';
    });

    const onAux = (e: MouseEvent) => {
      if (e.button === 1 && hovered) {
        e.preventDefault();
        void openUrl(hovered, true);
      }
    };
    const onMouseDown = (e: MouseEvent) => {
      noteUserInput();
      if (e.button === 1 && hovered) e.preventDefault();
    };
    el.addEventListener('auxclick', onAux);
    el.addEventListener('mousedown', onMouseDown);
    el.addEventListener('paste', noteUserInput, true);

    const sendSize = () => {
      if (!disposed) void request('terminals.resize', { id, cols: term.cols, rows: term.rows }).catch(() => {});
    };
    term.onResize(sendSize);
    // Several windows can show one terminal; the shell takes the size of the one in use.
    const onFocus = () => sendSize();
    window.addEventListener('focus', onFocus);
    const resizer = new ResizeObserver(() => {
      if (el.clientWidth > 0 && el.clientHeight > 0) fit.fit();
    });
    resizer.observe(el);

    term.onData((data) => {
      // xterm also sends its answers to queries in the output here. Only the window in use sends them, and never
      // for a replay, so a second window showing the same terminal doesn't answer twice.
      const fromUser = performance.now() - userInputAt < 1000;
      if (replaying || disposed || !(fromUser || document.hasFocus())) return;
      void request('terminals.input', { id, data }).catch((e: Error) => errorRef.current(e.message));
    });

    const apply = (p: TerminalOutputEvent) => {
      if (p.seq <= lastSeq) return;
      lastSeq = p.seq;
      term.write(p.data);
    };
    const offOutput = on('terminals.output', (p) => {
      if (p.id !== id) return;
      if (lastSeq < 0) early.push(p);
      else apply(p);
    });

    request('terminals.attach', { id })
      .then((snapshot) => {
        if (disposed) return;
        lastSeq = snapshot.seq;
        term.write(snapshot.data, () => {
          replaying = false;
          if (restoredRef.current) showRestoreDivider(term);
        });
        early.forEach(apply);
        early.length = 0;
        sendSize();
        term.focus();
      })
      .catch((e: Error) => errorRef.current(e.message));

    return () => {
      disposed = true;
      offOutput();
      resizer.disconnect();
      window.removeEventListener('focus', onFocus);
      el.removeEventListener('auxclick', onAux);
      el.removeEventListener('mousedown', onMouseDown);
      el.removeEventListener('paste', noteUserInput, true);
      hoverRef.current(null);
      term.dispose();
    };
  }, [id]);

  return <div className="xterm-box" ref={box} />;
}
