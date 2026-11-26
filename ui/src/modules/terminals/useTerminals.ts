import { useEffect, useState } from 'react';
import { on, request, type TerminalDto } from '../../bridge';

/** The host's terminal tabs, refetched whenever one opens, closes, exits or changes title, folder or task. */
export function useTerminals(): TerminalDto[] {
  const [terminals, setTerminals] = useState<TerminalDto[]>([]);

  useEffect(() => {
    let alive = true;
    const load = () =>
      request('terminals.list', {})
        .then((list) => alive && setTerminals(list))
        .catch(() => {});
    load();
    const off = on('terminals.changed', load);
    return () => {
      alive = false;
      off();
    };
  }, []);

  return terminals;
}

/** A short name for a tab: its startup command, else the title the program set, else the shell. */
export function terminalName(t: TerminalDto): string {
  if (t.startupCommand) return t.startupCommand;
  // Shells often set the title to their own exe path, which says nothing the shell name doesn't.
  if (t.title && !/\.exe$/i.test(t.title)) return t.title;
  return t.profileName;
}

export function folderLeaf(path: string): string {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts[parts.length - 1] ?? path;
}
