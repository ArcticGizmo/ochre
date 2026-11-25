import * as monaco from 'monaco-editor';
import EditorWorker from 'monaco-editor/editor/editor.worker.js?worker';

// Only the base editor worker is bundled; language services (TS, JSON) are out of scope for the spike.
self.MonacoEnvironment = {
  getWorker: () => new EditorWorker(),
};

export { monaco };
