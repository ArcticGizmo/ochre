import { useEffect, useRef } from 'react';
import { fitTerminal, mountTerminal } from '../terminal';

export function TerminalPanel({ visible }: { visible: boolean }) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (ref.current) mountTerminal(ref.current);
    const onResize = () => fitTerminal();
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  useEffect(() => {
    if (visible) requestAnimationFrame(fitTerminal);
  }, [visible]);

  return <div className="terminal" ref={ref} />;
}
