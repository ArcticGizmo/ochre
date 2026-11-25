import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// Monaco injects its styles at runtime, hence 'unsafe-inline' for styles only. Scripts stay 'self'.
const csp = [
  "default-src 'none'",
  "script-src 'self'",
  "style-src 'self' 'unsafe-inline'",
  "font-src 'self' data:",
  "img-src 'self' data:",
  "worker-src 'self'",
  "connect-src 'self'",
].join('; ');

// Only the built app gets the CSP; Vite's dev server needs inline scripts for hot reload.
const cspPlugin: Plugin = {
  name: 'ochre-csp',
  apply: 'build',
  transformIndexHtml: (html) =>
    html.replace('<head>', `<head>\n    <meta http-equiv="Content-Security-Policy" content="${csp}" />`),
};

export default defineConfig({
  base: './',
  plugins: [react(), cspPlugin],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 8000,
  },
  worker: {
    format: 'es',
  },
});
