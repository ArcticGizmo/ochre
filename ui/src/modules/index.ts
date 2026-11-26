import { gitUi } from './git';
import { roostUi } from './roost';
import { sprigUi } from './sprig';
import { terminalsUi } from './terminals';
import type { ModuleUi } from './types';

export type * from './types';

/**
 * Every first-party module's UI (ADR 0012). The app shows only those the host reports as running in `app.info`, so
 * a module switched off leaves no trace in the window.
 */
const all: ModuleUi[] = [gitUi, roostUi, sprigUi, terminalsUi];

export function runningModules(ids: readonly string[]): ModuleUi[] {
  return all.filter((m) => ids.includes(m.id));
}
