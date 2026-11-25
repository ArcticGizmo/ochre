import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { SerializeAddon } from '@xterm/addon-serialize';
import { on, request } from './bridge';

// One terminal for the page's lifetime; the host owns the PTY and keeps it alive across windows.
export const latency = { samples: [] as number[], pendingSince: 0 };

let term: Terminal | null = null;
let fit: FitAddon | null = null;
let serializer: SerializeAddon | null = null;

export function mountTerminal(container: HTMLElement): void {
  if (!term) {
    term = new Terminal({
      fontFamily: '"Cascadia Mono", Consolas, monospace',
      fontSize: 13,
      scrollback: 10000,
      cursorBlink: true,
      theme: { background: '#1b1a19' },
    });
    fit = new FitAddon();
    serializer = new SerializeAddon();
    term.loadAddon(fit);
    term.loadAddon(serializer);

    on('pty.output', ({ data }) => {
      if (latency.pendingSince) {
        latency.samples.push(performance.now() - latency.pendingSince);
        if (latency.samples.length > 200) latency.samples.shift();
        latency.pendingSince = 0;
      }
      term!.write(data);
    });
    on('pty.exit', ({ code }) => term!.write(`\r\n[process exited with ${code}]\r\n`));

    term.onData((data) => {
      if (!latency.pendingSince) latency.pendingSince = performance.now();
      void request('pty.input', { data }).catch(() => {});
    });
    term.onResize(({ cols, rows }) => void request('pty.resize', { cols, rows }).catch(() => {}));
  }

  if (term.element) {
    container.appendChild(term.element);
  } else {
    term.open(container);
  }
  fit!.fit();
  void request('pty.start', { cols: term.cols, rows: term.rows }).catch((e) => term!.write(`\r\n[failed to start: ${String(e)}]\r\n`));
}

export function fitTerminal(): void {
  fit?.fit();
}

/** Resolves once xterm has parsed everything written so far. */
export function drained(): Promise<void> {
  return new Promise((resolve) => (term ? term.write('', resolve) : resolve()));
}

export function serializeScrollback(): string {
  return serializer?.serialize() ?? '';
}
