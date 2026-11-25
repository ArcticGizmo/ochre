// Typed client for the host bridge. These types mirror the C# records in src/Ochre.Host/Bridge by hand for now;
// ADR 0009 (M1 slice 3) decides whether to generate them.

type Empty = Record<string, never>;
export interface Ack { ok: boolean }
export interface AppInfo { version: string; profile: string }

interface Requests {
  'app.info': { req: Empty; res: AppInfo };
  'app.ready': { req: Empty; res: Ack };
}

interface Events {
  'window.shown': Empty;
}

interface WebView2Bridge {
  postMessage(message: string): void;
  addEventListener(type: 'message', listener: (e: MessageEvent) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView2Bridge };
  }
}

interface Transport {
  send(message: string): void;
  listen(onMessage: (message: string) => void): void;
}

function detectTransport(): Transport {
  const webview = window.chrome?.webview;
  if (webview) {
    return {
      send: (m) => webview.postMessage(m),
      listen: (cb) => webview.addEventListener('message', (e) => cb(typeof e.data === 'string' ? e.data : JSON.stringify(e.data))),
    };
  }
  return {
    send: () => {
      throw new Error('No host bridge: open this page inside Ochre.');
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
