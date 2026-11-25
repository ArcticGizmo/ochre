/**
 * A path in a one-line space: when it doesn't fit, the leading directories are elided and the file name stays
 * visible. The full path is in the tooltip.
 */
export function PathText({ path }: { path: string }) {
  return (
    <span className="path" title={path}>
      <span dir="ltr">{path}</span>
    </span>
  );
}
