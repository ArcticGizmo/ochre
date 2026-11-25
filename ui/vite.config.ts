import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// The renderer is untrusted (docs/security.md, threat 5): no inline or remote script, no network.
// 'unsafe-inline' for styles only, which React's style props and later Monaco need.
const csp = [
  "default-src 'none'",
  "script-src 'self'",
  "style-src 'self' 'unsafe-inline'",
  "font-src 'self' data:",
  "img-src 'self' data:",
  "worker-src 'self'",
  "connect-src 'self'",
  "base-uri 'none'",
  "form-action 'none'",
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
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
  },
});
