import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@xterm/xterm/css/xterm.css';
import './styles.css';
import { App } from './App';
import { on, request } from './bridge';

const scriptStart = performance.now();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

// Lets the host time "window open to usable UI", both for a fresh page and a re-shown hidden window.
const reportReady = () => void request('app.ready', {}).catch(() => {});

requestAnimationFrame(() =>
  requestAnimationFrame(() => {
    reportReady();
    // Splits the page's share of window-open time from the host's (environment, controller, navigation start).
    const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined;
    const line =
      `page timings (ms since navigation start): response end ${nav?.responseEnd.toFixed(0) ?? '?'}, ` +
      `script start ${scriptStart.toFixed(0)}, dom interactive ${nav?.domInteractive.toFixed(0) ?? '?'}, ready ${performance.now().toFixed(0)}`;
    void request('bench.report', { line }).catch(() => {});
  }),
);
on('window.shown', () => requestAnimationFrame(reportReady));
