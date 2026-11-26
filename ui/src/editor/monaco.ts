// Monaco with only what Ochre's editor uses: the editor API, the built-in tokenisers for syntax colours, and the
// editing features (find, folding, multiple cursors, comments, the diff editor). The language services and the LSP
// client in Monaco's full entry stay out (vision: good, not an IDE), which keeps this chunk small and its one worker
// generic. Loaded on first use through loadMonaco.ts, never on the window-open path.
import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/basic-languages/monaco.contribution';
import 'monaco-editor/editor/browser/coreCommands';
import 'monaco-editor/editor/browser/widget/diffEditor/diffEditor.contribution';
import 'monaco-editor/editor/common/standaloneStrings';
import 'monaco-editor/editor/contrib/bracketMatching/browser/bracketMatching';
import 'monaco-editor/editor/contrib/clipboard/browser/clipboard';
import 'monaco-editor/editor/contrib/comment/browser/comment';
import 'monaco-editor/editor/contrib/contextmenu/browser/contextmenu';
import 'monaco-editor/editor/contrib/cursorUndo/browser/cursorUndo';
import 'monaco-editor/editor/contrib/dnd/browser/dnd';
import 'monaco-editor/editor/contrib/folding/browser/folding';
import 'monaco-editor/editor/contrib/hover/browser/hoverContribution';
import 'monaco-editor/editor/contrib/indentation/browser/indentation';
import 'monaco-editor/editor/contrib/linesOperations/browser/linesOperations';
import 'monaco-editor/editor/contrib/links/browser/links';
import 'monaco-editor/editor/contrib/multicursor/browser/multicursor';
import 'monaco-editor/editor/contrib/readOnlyMessage/browser/contribution';
import 'monaco-editor/editor/contrib/smartSelect/browser/smartSelect';
import 'monaco-editor/editor/contrib/tokenization/browser/tokenization';
import 'monaco-editor/editor/contrib/unicodeHighlighter/browser/unicodeHighlighter';
import 'monaco-editor/editor/contrib/wordHighlighter/browser/wordHighlighter';
import 'monaco-editor/editor/contrib/wordOperations/browser/wordOperations';
import 'monaco-editor/editor/standalone/browser/quickAccess/standaloneGotoLineQuickAccess';
import 'monaco-editor/features/find/register';
import 'monaco-codicon.css';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';

self.MonacoEnvironment = { getWorker: () => new EditorWorker() };

// Ochre's own colours (styles.css), so the editor reads as part of the window rather than VS Code's grey.
monaco.editor.defineTheme('ochre', {
  base: 'vs-dark',
  inherit: true,
  rules: [
    { token: 'comment', foreground: '7f7a74', fontStyle: 'italic' },
    { token: 'keyword', foreground: 'e8974a' },
    { token: 'type', foreground: '8fbcd4' },
    { token: 'type.identifier', foreground: '8fbcd4' },
    { token: 'string', foreground: 'a8c97f' },
    { token: 'number', foreground: 'd9b44a' },
    { token: 'regexp', foreground: 'd9b44a' },
    { token: 'tag', foreground: 'e8974a' },
    { token: 'attribute.name', foreground: '8fbcd4' },
    { token: 'attribute.value', foreground: 'a8c97f' },
    { token: 'keyword.md', foreground: 'e8974a', fontStyle: 'bold' },
  ],
  colors: {
    'editor.background': '#1b1a19',
    'editor.foreground': '#d6d1cb',
    'editor.lineHighlightBackground': '#242322',
    'editor.lineHighlightBorder': '#00000000',
    'editor.selectionBackground': '#5a4128',
    'editor.inactiveSelectionBackground': '#3d3126',
    'editorCursor.foreground': '#e8974a',
    'editorLineNumber.foreground': '#5f5a55',
    'editorLineNumber.activeForeground': '#a39d96',
    'editorIndentGuide.background1': '#2a2928',
    'editorWidget.background': '#242322',
    'editorWidget.border': '#3a3836',
    'editorHoverWidget.background': '#242322',
    'editorHoverWidget.border': '#3a3836',
    'input.background': '#1b1a19',
    'input.border': '#3a3836',
    'focusBorder': '#cc7722',
    'scrollbarSlider.background': '#3a383680',
    'scrollbarSlider.hoverBackground': '#5c5854a0',
    'diffEditor.insertedLineBackground': '#86b57224',
    'diffEditor.removedLineBackground': '#e8877d21',
    'diffEditor.insertedTextBackground': '#86b57240',
    'diffEditor.removedTextBackground': '#e8877d40',
    'diffEditor.diagonalFill': '#2a2928',
  },
});

/** Monaco's language id for a file, from its name or extension; plain text when none matches. */
export function languageFor(path: string): string {
  const name = (path.split('/').pop() ?? path).toLowerCase();
  const dot = name.lastIndexOf('.');
  const ext = dot >= 0 ? name.slice(dot) : '';
  for (const lang of monaco.languages.getLanguages()) {
    if (lang.filenames?.some((f) => f.toLowerCase() === name)) return lang.id;
    if (ext && lang.extensions?.some((e) => e.toLowerCase() === ext)) return lang.id;
  }
  return 'plaintext';
}

export { monaco };
export type Monaco = typeof monaco;
