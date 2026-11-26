import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// Monaco's package exports map every subpath to a .js file, so its codicon stylesheet is reached by its real path
// (root-relative, which Vite resolves against the project folder).
const codiconCss = '/node_modules/monaco-editor/esm/vs/base/browser/ui/codicons/codicon/codicon.css';

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
  resolve: {
    alias: [{ find: /^monaco-codicon\.css$/, replacement: codiconCss }],
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // Monaco is one large chunk by nature, loaded only when the editor is first used.
    chunkSizeWarningLimit: 4000,
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
  },
});
