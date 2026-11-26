import { useEffect, useRef } from 'react';
import type { editor } from 'monaco-editor/editor/editor.api';
import { useMonaco } from './loadMonaco';

const baseOptions: editor.IStandaloneEditorConstructionOptions = {
  theme: 'ochre',
  automaticLayout: true,
  fontFamily: "'Cascadia Mono', Consolas, monospace",
  fontSize: 13,
  lineHeight: 20,
  minimap: { enabled: false },
  scrollBeyondLastLine: false,
  renderWhitespace: 'selection',
  fixedOverflowWidgets: true,
  // Hover cards and link previews would load nothing useful without language services.
  hover: { enabled: 'off' },
};

interface CodeViewProps {
  model: editor.ITextModel;
  readOnly: boolean;
  /** Restored when the model is shown, and handed back before it's swapped out. */
  viewState: editor.ICodeEditorViewState | null;
  onViewState: (state: editor.ICodeEditorViewState | null) => void;
  /** A line to bring into view and put the cursor on, once per change of `reveal`. */
  reveal?: { line: number; at: number } | null;
  onSave?: () => void;
  onCursor?: (line: number, column: number) => void;
}

/** One Monaco editor showing a model the caller owns; the model outlives the view (switching tabs or views). */
export function CodeView({ model, readOnly, viewState, onViewState, reveal, onSave, onCursor }: CodeViewProps) {
  const m = useMonaco();
  const host = useRef<HTMLDivElement>(null);
  const ed = useRef<editor.IStandaloneCodeEditor | null>(null);
  const save = useRef(onSave);
  save.current = onSave;
  const keep = useRef(onViewState);
  keep.current = onViewState;
  const cursor = useRef(onCursor);
  cursor.current = onCursor;

  useEffect(() => {
    if (!m || !host.current) return;
    const e = m.monaco.editor.create(host.current, { ...baseOptions, model: null });
    e.addCommand(m.monaco.KeyMod.CtrlCmd | m.monaco.KeyCode.KeyS, () => save.current?.());
    e.onDidChangeCursorPosition((c) => cursor.current?.(c.position.lineNumber, c.position.column));
    ed.current = e;
    return () => {
      keep.current(e.saveViewState());
      e.dispose();
      ed.current = null;
    };
  }, [m]);

  useEffect(() => {
    const e = ed.current;
    if (!e) return;
    e.setModel(model);
    if (viewState) e.restoreViewState(viewState);
    e.focus();
    return () => {
      if (ed.current !== e || !e.getModel()) return;
      keep.current(e.saveViewState());
    };
    // The view state is read only when this model is shown; later changes to it come from this editor.
  }, [m, model]);

  useEffect(() => {
    ed.current?.updateOptions({ readOnly });
  }, [m, readOnly]);

  useEffect(() => {
    const e = ed.current;
    if (!e || !reveal) return;
    const line = Math.max(1, Math.min(reveal.line, model.getLineCount()));
    e.setPosition({ lineNumber: line, column: 1 });
    e.revealLineInCenter(line);
    e.focus();
  }, [m, model, reveal]);

  return <div className="code-view" ref={host}>{!m && <p className="empty code-loading">Loading the editor…</p>}</div>;
}

interface DiffViewProps {
  /** The left side's text; read-only. */
  original: string;
  /** The right side: a model the caller owns (editable unless `readOnly`), or text for a read-only diff. */
  modified: editor.ITextModel | string;
  language: string;
  readOnly: boolean;
  sideBySide: boolean;
  onSave?: () => void;
}

/** Monaco's diff editor: what changed from `original` to `modified`. */
export function DiffView({ original, modified, language, readOnly, sideBySide, onSave }: DiffViewProps) {
  const m = useMonaco();
  const host = useRef<HTMLDivElement>(null);
  const ed = useRef<editor.IStandaloneDiffEditor | null>(null);
  const save = useRef(onSave);
  save.current = onSave;

  useEffect(() => {
    if (!m || !host.current) return;
    const e = m.monaco.editor.createDiffEditor(host.current, {
      ...baseOptions,
      originalEditable: false,
      renderSideBySide: sideBySide,
      useInlineViewWhenSpaceIsLimited: true,
      hideUnchangedRegions: { enabled: true, contextLineCount: 3, minimumLineCount: 6, revealLineCount: 20 },
    });
    e.getModifiedEditor().addCommand(m.monaco.KeyMod.CtrlCmd | m.monaco.KeyCode.KeyS, () => save.current?.());
    ed.current = e;
    return () => {
      e.dispose();
      ed.current = null;
    };
    // Layout changes go through updateOptions below; recreating would lose the scroll position.
  }, [m]);

  useEffect(() => {
    const e = ed.current;
    if (!m || !e) return;
    const left = m.monaco.editor.createModel(original, language);
    const own = typeof modified === 'string';
    const right = own ? m.monaco.editor.createModel(modified, language) : modified;
    e.setModel({ original: left, modified: right });
    return () => {
      // On unmount the editor is disposed first (its effect's cleanup runs before this one).
      if (ed.current === e) e.setModel(null);
      left.dispose();
      if (own) right.dispose();
    };
  }, [m, original, modified, language]);

  useEffect(() => {
    ed.current?.updateOptions({ renderSideBySide: sideBySide, readOnly });
  }, [m, sideBySide, readOnly]);

  return <div className="code-view" ref={host}>{!m && <p className="empty code-loading">Loading the editor…</p>}</div>;
}
