import { useEffect, useRef, useState } from 'react';
import { request } from '../bridge';
import { monaco } from '../monaco';

export function EditorPanel({ visible }: { visible: boolean }) {
  const host = useRef<HTMLDivElement>(null);
  const editor = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const diff = useRef<monaco.editor.IStandaloneDiffEditor | null>(null);
  const original = useRef<monaco.editor.ITextModel | null>(null);
  const current = useRef<monaco.editor.ITextModel | null>(null);
  const [path, setPath] = useState('');
  const [showDiff, setShowDiff] = useState(false);
  const [status, setStatus] = useState('Enter an absolute path and press Open.');

  useEffect(() => {
    return () => {
      editor.current?.dispose();
      diff.current?.dispose();
      original.current?.dispose();
      current.current?.dispose();
    };
  }, []);

  useEffect(() => {
    if (!visible) return;
    editor.current?.layout();
    diff.current?.layout();
  }, [visible]);

  useEffect(() => {
    if (!host.current || !current.current || !original.current) return;
    editor.current?.dispose();
    diff.current?.dispose();
    editor.current = null;
    diff.current = null;
    const options = { automaticLayout: true, theme: 'vs-dark', fontSize: 13 } as const;
    if (showDiff) {
      diff.current = monaco.editor.createDiffEditor(host.current, { ...options, originalEditable: false });
      diff.current.setModel({ original: original.current, modified: current.current });
    } else {
      editor.current = monaco.editor.create(host.current, { ...options, model: current.current });
    }
  }, [showDiff, status]);

  async function open() {
    try {
      const t0 = performance.now();
      const file = await request('fs.read', { path });
      original.current?.dispose();
      current.current?.dispose();
      original.current = monaco.editor.createModel(file.content, file.language);
      current.current = monaco.editor.createModel(file.content, file.language);
      setStatus(`${file.path} (${file.language}, ${file.content.length.toLocaleString()} chars) read in ${(performance.now() - t0).toFixed(1)} ms`);
    } catch (e) {
      setStatus(`Failed: ${(e as Error).message}`);
    }
  }

  return (
    <div className="editor-panel">
      <div className="toolbar">
        <input value={path} onChange={(e) => setPath(e.target.value)} placeholder="C:\path\to\file.cs" onKeyDown={(e) => e.key === 'Enter' && void open()} />
        <button onClick={() => void open()}>Open</button>
        <label>
          <input type="checkbox" checked={showDiff} onChange={(e) => setShowDiff(e.target.checked)} /> Diff against disk
        </label>
        <span className="status">{status}</span>
      </div>
      <div className="monaco" ref={host} />
    </div>
  );
}
