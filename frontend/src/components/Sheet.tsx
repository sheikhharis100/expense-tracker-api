import { useEffect, useRef, type ReactNode } from 'react';
import { Icon } from './Icon';

// Bottom sheet on phones, centred dialog on larger screens.
export function Sheet({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    document.body.classList.add('sheet-open');
    const first = panelRef.current?.querySelector<HTMLElement>('input, select, textarea, button:not(.sheet-close)');
    first?.focus();
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.classList.remove('sheet-open');
      previous?.focus?.();
    };
  }, [onClose]);

  return (
    <div className="sheet-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className={`sheet ${wide ? 'sheet-wide' : ''}`} role="dialog" aria-modal="true" aria-label={title} ref={panelRef}>
        <div className="sheet-grip" aria-hidden="true" />
        <div className="sheet-head">
          <h2>{title}</h2>
          <button type="button" className="btn-ghost btn-round sheet-close" aria-label="Close" onClick={onClose}>
            <Icon name="x" />
          </button>
        </div>
        <div className="sheet-body">{children}</div>
      </div>
    </div>
  );
}

// Asks before an action whose effect depends on scope (this month only vs this and future months).
export function ScopeChoice({
  value,
  onChange,
  month,
  futureLabel = 'This and future months',
}: {
  value: 'month' | 'future';
  onChange: (v: 'month' | 'future') => void;
  month: string;
  futureLabel?: string;
}) {
  return (
    <fieldset className="field">
      <legend>Apply to</legend>
      <div className="segmented" role="radiogroup">
        <button type="button" role="radio" aria-checked={value === 'month'} className={value === 'month' ? 'on' : ''} onClick={() => onChange('month')}>
          {month} only
        </button>
        <button type="button" role="radio" aria-checked={value === 'future'} className={value === 'future' ? 'on' : ''} onClick={() => onChange('future')}>
          {futureLabel}
        </button>
      </div>
    </fieldset>
  );
}
