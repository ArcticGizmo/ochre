// Typed client for the host bridge. Hand-written mirrors of Spike.Core/Bridge/Messages.cs; the spike
// records how painful keeping these in sync is.

export interface TaskSummary { id: string; title: string; state: string; body: string }
export interface ReadFileResult { path: string; content: string; language: string }
export interface Metrics { shell: string; coldStartMs: number; lastWindowOpenMs: number | null; hostWorkingSetMb: number }
type Empty = Record<string, never>;
interface Ack { ok: boolean }

interface Requests {
  'app.ready': { req: Empty; res: Ack };
  'metrics.get': { req: Empty; res: Metrics };
  'bench.echo': { req: { n: number; s: string }; res: { n: number; s: string } };
  'bench.makeBigFile': { req: Empty; res: { path: string; bytes: number } };
  'tasks.list': { req: Empty; res: TaskSummary[] };
  'fs.read': { req: { path: string }; res: ReadFileResult };
  'pty.start': { req: { cols: number; rows: number }; res: { attached: boolean; pid: number } };
  'pty.input': { req: { data: string }; res: Ack };
  'pty.resize': { req: { cols: number; rows: number }; res: Ack };
  'pty.setMode': { req: { coalesceMs: number }; res: Ack };
}

interface Events {
  'pty.output': { data: string };
  'pty.exit': { code: number };
  'window.shown': Empty;
}

interface Transport {
  send(message: string): void;
  listen(onMessage: (message: string) => void): void;
}

interface WebView2Bridge {
  postMessage(message: string): void;
  addEventListener(type: 'message', listener: (e: MessageEvent) => void): void;
}

interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView2Bridge };
  }
}

function detectTransport(): Transport {
  const webview = window.chrome?.webview;
  if (webview) {
    return {
      send: (m) => webview.postMessage(m),
      listen: (cb) => webview.addEventListener('message', (e) => cb(typeof e.data === 'string' ? e.data : JSON.stringify(e.data))),
    };
  }
  const external = (window as unknown as { external?: Partial<PhotinoExternal> }).external;
  if (external?.sendMessage && external.receiveMessage) {
    const ext = external as PhotinoExternal;
    return { send: (m) => ext.sendMessage(m), listen: (cb) => ext.receiveMessage(cb) };
  }
  return {
    send: () => {
      throw new Error('No host bridge: open this page inside an Ochre spike shell.');
    },
    listen: () => {},
  };
}

const transport = detectTransport();
const pending = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
const listeners = new Map<string, Set<(payload: unknown) => void>>();
let nextId = 1;

transport.listen((raw) => {
  let msg: { id?: number; ok?: boolean; result?: unknown; error?: string; event?: string; payload?: unknown };
  try {
    msg = JSON.parse(raw);
  } catch {
    return;
  }
  if (typeof msg.event === 'string') {
    listeners.get(msg.event)?.forEach((cb) => cb(msg.payload));
    return;
  }
  if (typeof msg.id !== 'number') return;
  const p = pending.get(msg.id);
  if (!p) return;
  pending.delete(msg.id);
  if (msg.ok) p.resolve(msg.result);
  else p.reject(new Error(msg.error ?? 'request failed'));
});

export function request<K extends keyof Requests>(type: K, payload: Requests[K]['req']): Promise<Requests[K]['res']> {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    try {
      transport.send(JSON.stringify({ id, type, payload }));
    } catch (e) {
      pending.delete(id);
      reject(e instanceof Error ? e : new Error(String(e)));
    }
  });
}

export function on<K extends keyof Events>(event: K, cb: (payload: Events[K]) => void): () => void {
  let set = listeners.get(event);
  if (!set) listeners.set(event, (set = new Set()));
  const listener = cb as (payload: unknown) => void;
  set.add(listener);
  return () => set.delete(listener);
}
