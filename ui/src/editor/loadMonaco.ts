import { useEffect, useState } from 'react';

export type MonacoModule = typeof import('./monaco');

let loading: Promise<MonacoModule> | null = null;
let loaded: MonacoModule | null = null;

/** Monaco, loaded once on first use as its own chunk. */
export function loadMonaco(): Promise<MonacoModule> {
  loading ??= import('./monaco').then((m) => (loaded = m));
  return loading;
}

/** Monaco once it has loaded (starting the load if needed); null until then. */
export function useMonaco(): MonacoModule | null {
  const [m, setM] = useState<MonacoModule | null>(loaded);
  useEffect(() => {
    if (m) return;
    let live = true;
    loadMonaco().then((x) => live && setM(x));
    return () => {
      live = false;
    };
  }, [m]);
  return m;
}
