import { useEffect, useRef, useState } from 'react';
import { on, request } from '../bridge';
import { drained, latency, parsed, serializeScrollback } from '../terminal';

function percentile(sorted: number[], p: number): number {
  if (sorted.length === 0) return NaN;
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
}

function summarise(samples: number[]): string {
  const s = [...samples].sort((a, b) => a - b);
  return `n=${s.length} median=${percentile(s, 50).toFixed(3)} ms p99=${percentile(s, 99).toFixed(3)} ms max=${(s[s.length - 1] ?? NaN).toFixed(3)} ms`;
}

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

/** Watches the main thread while a run is in flight, so a frozen UI shows up as a long frame gap. */
function startJankWatch() {
  let maxGap = 0;
  let maxLongTask = 0;
  let last = performance.now();
  let running = true;
  const tick = (t: number) => {
    maxGap = Math.max(maxGap, t - last);
    last = t;
    if (running) requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
  let observer: PerformanceObserver | null = null;
  try {
    observer = new PerformanceObserver((list) => list.getEntries().forEach((e) => (maxLongTask = Math.max(maxLongTask, e.duration))));
    observer.observe({ type: 'longtask', buffered: false });
  } catch {
    observer = null;
  }
  return () => {
    running = false;
    observer?.disconnect();
    return { maxFrameGapMs: maxGap, maxLongTaskMs: maxLongTask };
  };
}

function nextOutput(): Promise<void> {
  return new Promise((resolve) => {
    const off = on('pty.output', () => {
      off();
      resolve();
    });
  });
}

async function echoBench(): Promise<string> {
  const payload = 'x'.repeat(32);
  for (let i = 0; i < 200; i++) await request('bench.echo', { n: i, s: payload });
  const samples: number[] = [];
  const t0 = performance.now();
  for (let i = 0; i < 10000; i++) {
    const t = performance.now();
    await request('bench.echo', { n: i, s: payload });
    samples.push(performance.now() - t);
  }
  return `bridge echo x10k sequential: total=${(performance.now() - t0).toFixed(0)} ms ${summarise(samples)}`;
}

/** Sends a key to the shell and times until the shell's echo arrives; a stand-in for key-to-glyph. */
async function simulatedTyping(): Promise<string> {
  const samples: number[] = [];
  for (let i = 0; i < 30; i++) {
    for (const key of ['a', '\x7f']) {
      const t = performance.now();
      const echoed = nextOutput();
      await request('pty.input', { data: key });
      await echoed;
      samples.push(performance.now() - t);
      await sleep(40);
    }
  }
  return `simulated typing (input sent to echo received): ${summarise(samples)}`;
}

/** Sends a command to the terminal and times output until it has been quiet for idleMs. */
async function throughput(label: string, command: string, coalesceMs: number, idleMs = 3000): Promise<string> {
  let bytes = 0;
  let messages = 0;
  let lastAt = 0;
  const off = on('pty.output', ({ data }) => {
    lastAt = performance.now();
    bytes += data.length;
    messages++;
  });
  const stopJank = startJankWatch();
  const start = performance.now();
  await request('pty.input', { data: command + '\r' });
  await new Promise<void>((resolve) => {
    const check = () => (lastAt && performance.now() - lastAt > idleMs ? resolve() : setTimeout(check, 100));
    setTimeout(check, 500);
  });
  off();
  await drained();
  const jank = stopJank();
  return (
    `${label} [coalesce ${coalesceMs} ms]: delivered ${(lastAt - start).toFixed(0)} ms, parsed by xterm ${(parsed.lastAt - start).toFixed(0)} ms, ` +
    `${(bytes / 1024 / 1024).toFixed(1)} M chars in ${messages} messages, max frame gap ${jank.maxFrameGapMs.toFixed(0)} ms, ` +
    `max long task ${jank.maxLongTaskMs.toFixed(0)} ms`
  );
}

async function metricsLine(): Promise<string> {
  const m = await request('metrics.get', {});
  return (
    `shell=${m.shell} cold start to tray=${m.coldStartMs.toFixed(0)} ms, last window open=${m.lastWindowOpenMs?.toFixed(0) ?? 'n/a'} ms, ` +
    `host working set=${m.hostWorkingSetMb.toFixed(0)} MB (WebView processes not included; use measure.ps1)`
  );
}

const defaultDir = '$env:USERPROFILE\\.nuget\\packages';

export function BenchPanel() {
  const [log, setLog] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [dir, setDir] = useState(defaultDir);
  const [coalesce, setCoalesce] = useState(0);
  const autoStarted = useRef(false);

  const write = (line: string) => {
    setLog((l) => [...l, `${new Date().toLocaleTimeString()}  ${line}`]);
    void request('bench.report', { line }).catch(() => {});
  };

  async function run(name: string, fn: () => Promise<string | void>) {
    setBusy(true);
    try {
      const line = await fn();
      if (line) write(line);
    } catch (e) {
      write(`${name} failed: ${(e as Error).message}`);
    } finally {
      setBusy(false);
    }
  }

  async function setMode(ms: number) {
    await request('pty.setMode', { coalesceMs: ms });
    setCoalesce(ms);
  }

  async function catBigFile(coalesceMs: number) {
    const f = await request('bench.makeBigFile', {});
    return throughput('cat 50 MB', `cmd /c type "${f.path}"`, coalesceMs);
  }

  // Runs the whole suite unattended when the shell was started with OCHRE_SPIKE_AUTOBENCH=1.
  useEffect(() => {
    if (autoStarted.current || !new URLSearchParams(location.search).has('autobench')) return;
    autoStarted.current = true;
    void run('autobench', async () => {
      await sleep(3000);
      write('autobench: start');
      write(await metricsLine());
      write(await echoBench());
      write(await simulatedTyping());
      for (const ms of [0, 16]) {
        await setMode(ms);
        await request('pty.input', { data: 'cls\r' });
        await sleep(1000);
        write(await throughput('dir listing', `Get-ChildItem -Recurse "${defaultDir}"`, ms));
        await request('pty.input', { data: 'cls\r' });
        await sleep(1000);
        write(await catBigFile(ms));
      }
      const t0 = performance.now();
      const text = serializeScrollback();
      write(`serialize scrollback: ${(text.length / 1024).toFixed(0)} KB in ${(performance.now() - t0).toFixed(1)} ms`);
      return 'autobench: done';
    });
  }, []);

  return (
    <div className="bench">
      <div className="toolbar">
        <button disabled={busy} onClick={() => void run('metrics', metricsLine)}>Metrics</button>
        <button disabled={busy} onClick={() => void run('echo', echoBench)}>Bridge echo x10k</button>
        <button disabled={busy} onClick={() => write(`typing latency (key to first echoed output): ${summarise(latency.samples)}`)}>Typing latency</button>
        <button disabled={busy} onClick={() => void run('typing', simulatedTyping)}>Simulated typing</button>
        <button
          disabled={busy}
          onClick={() => {
            const t0 = performance.now();
            const text = serializeScrollback();
            write(`serialize scrollback: ${(text.length / 1024).toFixed(0)} KB in ${(performance.now() - t0).toFixed(1)} ms`);
          }}
        >
          Serialize scrollback
        </button>
      </div>
      <div className="toolbar">
        <span>Output delivery:</span>
        <button disabled={busy || coalesce === 0} onClick={() => void run('mode', () => setMode(0))}>Per read</button>
        <button disabled={busy || coalesce === 16} onClick={() => void run('mode', () => setMode(16))}>Coalesce 16 ms</button>
      </div>
      <div className="toolbar">
        <input value={dir} onChange={(e) => setDir(e.target.value)} />
        <button disabled={busy} onClick={() => void run('dir', () => throughput('dir listing', `Get-ChildItem -Recurse "${dir}"`, coalesce))}>Recursive listing</button>
        <button disabled={busy} onClick={() => void run('cat', () => catBigFile(coalesce))}>Cat 50 MB file</button>
      </div>
      <p className="hint">Throughput runs type into the terminal tab; switch to it to watch, or stay here to measure without rendering cost.</p>
      <div className="toolbar">
        <button onClick={() => void navigator.clipboard.writeText(log.join('\n'))}>Copy log</button>
        <button onClick={() => setLog([])}>Clear</button>
      </div>
      <pre className="log">{log.join('\n')}</pre>
    </div>
  );
}
