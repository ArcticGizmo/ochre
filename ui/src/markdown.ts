import DOMPurify from 'dompurify';
import { marked } from 'marked';

// Task notes are written by people and by agents, and agents relay text from tickets and web pages, so the HTML
// is sanitised before it reaches the DOM (docs/security.md, threats 2 and 5). Links stay links: clicking one is a
// navigation the host refuses and hands to the real browser.
export function renderMarkdown(text: string): string {
  const html = marked.parse(text, { async: false, gfm: true, breaks: false });
  return DOMPurify.sanitize(html, {
    USE_PROFILES: { html: true },
    FORBID_TAGS: ['style', 'form', 'input', 'button', 'iframe', 'img'],
    FORBID_ATTR: ['style'],
  });
}
