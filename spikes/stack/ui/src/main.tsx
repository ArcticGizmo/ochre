import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@xterm/xterm/css/xterm.css';
import './styles.css';
import { App } from './App';
import { on, request } from './bridge';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

// Lets the host time "window open to usable UI", both for a fresh page and a re-shown hidden window.
const reportReady = () => void request('app.ready', {}).catch(() => {});
requestAnimationFrame(() => requestAnimationFrame(reportReady));
on('window.shown', () => requestAnimationFrame(reportReady));
