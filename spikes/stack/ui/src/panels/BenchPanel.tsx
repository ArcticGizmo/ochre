import { useState } from 'react';
import { on, request } from '../bridge';
import { drained, latency, serializeScrollback } from '../terminal';

function percentile(sorted: number[], p: number): number {
  if (sorted.length === 0) return NaN;
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
}

function summarise(samples: number[]): string {
  const s = [...samples].sort((a, b) => a - b);
  return `n=${s.length} median=${percentile(s, 50).toFixed(3)} ms p99=${percentile(s, 99).toFixed(3)} ms max=${(s[s.length - 1] ?? NaN).toFixed(3)} ms`;
}

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

export function BenchPanel() {
  const [log, setLog] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [dir, setDir] = useState('$env:USERPROFILE\\.nuget\\packages');
  const [coalesce, setCoalesce] = useState(0);
  const write = (line: string) => setLog((l) => [...l, `${new Date().toLocaleTimeString()}  ${line}`]);

  async function run(name: string, fn: () => Promise<void>) {
    setBusy(true);
    try {
      await fn();
    } catch (e) {
      write(`${name} failed: ${(e as Error).message}`);
    } finally {
      setBusy(false);
    }
  }

  const echo = () =>
    run('echo', async () => {
      const payload = 'x'.repeat(32);
      for (let i = 0; i < 200; i++) await request('bench.echo', { n: i, s: payload });
      const samples: number[] = [];
      const t0 = performance.now();
      for (let i = 0; i < 10000; i++) {
        const t = performance.now();
        await request('bench.echo', { n: i, s: payload });
        samples.push(performance.now() - t);
      }
      write(`bridge echo x10k sequential: total=${(performance.now() - t0).toFixed(0)} ms ${summarise(samples)}`);
    });

  /** Sends a command to the terminal and times output until it has been quiet for 1.5 s. */
  const throughput = (label: string, command: string) =>
    run(label, async () => {
      let bytes = 0;
      let messages = 0;
      let first = 0;
      let lastAt = 0;
      const off = on('pty.output', ({ data }) => {
        const now = performance.now();
        if (!first) first = now;
        lastAt = now;
        bytes += data.length;
        messages++;
      });
      const stopJank = startJankWatch();
      const start = performance.now();
      await request('pty.input', { data: command + '\r' });
      await new Promise<void>((resolve) => {
        const check = () => (lastAt && performance.now() - lastAt > 1500 ? resolve() : setTimeout(check, 100));
        setTimeout(check, 500);
      });
      off();
      await drained();
      const rendered = performance.now();
      const jank = stopJank();
      write(
        `${label} [coalesce ${coalesce} ms]: output ${(lastAt - start).toFixed(0)} ms, rendered ${(rendered - start - 1500).toFixed(0)} ms (approx), ` +
          `${(bytes / 1024 / 1024).toFixed(1)} MB chars in ${messages} messages, max frame gap ${jank.maxFrameGapMs.toFixed(0)} ms, ` +
          `max long task ${jank.maxLongTaskMs.toFixed(0)} ms`,
      );
    });

  const bigFile = () =>
    run('big file', async () => {
      const f = await request('bench.makeBigFile', {});
      write(`big file ready: ${f.path} (${(f.bytes / 1024 / 1024).toFixed(1)} MB)`);
      await throughput('cat 50 MB', `cmd /c type "${f.path}"`);
    });

  const setMode = (ms: number) =>
    run('mode', async () => {
      await request('pty.setMode', { coalesceMs: ms });
      setCoalesce(ms);
      write(`terminal output coalescing set to ${ms} ms`);
    });

  const metrics = () =>
    run('metrics', async () => {
      const m = await request('metrics.get', {});
      write(
        `shell=${m.shell} cold start to tray=${m.coldStartMs.toFixed(0)} ms, last window open=${m.lastWindowOpenMs?.toFixed(0) ?? 'n/a'} ms, ` +
          `host working set=${m.hostWorkingSetMb.toFixed(0)} MB (WebView processes not included; use measure.ps1)`,
      );
    });

  const typing = () => write(`typing latency (key to first echoed output): ${summarise(latency.samples)}`);

  const serialize = () => {
    const t0 = performance.now();
    const text = serializeScrollback();
    write(`serialize scrollback: ${(text.length / 1024).toFixed(0)} KB in ${(performance.now() - t0).toFixed(1)} ms`);
  };

  return (
    <div className="bench">
      <div className="toolbar">
        <button disabled={busy} onClick={() => void metrics()}>Metrics</button>
        <button disabled={busy} onClick={() => void echo()}>Bridge echo x10k</button>
        <button disabled={busy} onClick={typing}>Typing latency</button>
        <button disabled={busy} onClick={serialize}>Serialize scrollback</button>
      </div>
      <div className="toolbar">
        <span>Output delivery:</span>
        <button disabled={busy || coalesce === 0} onClick={() => void setMode(0)}>Per read</button>
        <button disabled={busy || coalesce === 16} onClick={() => void setMode(16)}>Coalesce 16 ms</button>
      </div>
      <div className="toolbar">
        <input value={dir} onChange={(e) => setDir(e.target.value)} />
        <button disabled={busy} onClick={() => void throughput('dir listing', `Get-ChildItem -Recurse "${dir}"`)}>Recursive listing</button>
        <button disabled={busy} onClick={() => void bigFile()}>Cat 50 MB file</button>
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
