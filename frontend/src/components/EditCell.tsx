import { useRef, useState, type KeyboardEvent } from 'react';

type Parse<T> = (text: string) => { ok: true; value: T } | { ok: false; error: string };

interface Props<T> {
  // Shown when the cell isn't focused ("2,810").
  display: string;
  // Shown while editing ("=1810+1000").
  edit: string;
  parse: Parse<T>;
  isSame: (next: T) => boolean;
  onCommit: (value: T) => void;
  label: string;
  placeholder?: string;
  className?: string;
  align?: 'left' | 'right';
  inputMode?: 'decimal' | 'text';
  // Grid navigation: called with the key that should move focus (Enter / arrows) after committing.
  onNavigate?: (key: 'down' | 'up' | 'left' | 'right') => void;
  cellId?: string;
  readOnly?: boolean;
  // Form-style cells show their placeholder even when not focused; grid cells stay clean.
  idlePlaceholder?: boolean;
}

// The sheet's yellow cell: looks like text, edits in place, saves on Enter or when you leave it.
export function EditCell<T>({
  display,
  edit,
  parse,
  isSame,
  onCommit,
  label,
  placeholder,
  className = '',
  align = 'right',
  inputMode = 'decimal',
  onNavigate,
  cellId,
  readOnly,
  idlePlaceholder,
}: Props<T>) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');
  const [error, setError] = useState<string | null>(null);
  const skipBlurCommit = useRef(false);

  const commit = (): boolean => {
    const parsed = parse(draft);
    if (!parsed.ok) {
      setError(parsed.error);
      return false;
    }
    setError(null);
    if (!isSame(parsed.value)) onCommit(parsed.value);
    return true;
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    const input = e.currentTarget;
    if (e.key === 'Escape') {
      skipBlurCommit.current = true;
      setError(null);
      setDraft(edit);
      input.blur();
      return;
    }
    if (e.key === 'Enter') {
      e.preventDefault();
      if (!commit()) return;
      skipBlurCommit.current = true;
      if (onNavigate) onNavigate(e.shiftKey ? 'up' : 'down');
      else input.blur();
      return;
    }
    if (!onNavigate) return;
    const atStart = input.selectionStart === 0 && input.selectionEnd === 0;
    const atEnd = input.selectionStart === input.value.length;
    const move =
      e.key === 'ArrowDown' ? 'down' : e.key === 'ArrowUp' ? 'up' : e.key === 'ArrowLeft' && atStart ? 'left' : e.key === 'ArrowRight' && atEnd ? 'right' : null;
    if (move) {
      e.preventDefault();
      if (!commit()) return;
      skipBlurCommit.current = true;
      onNavigate(move);
    }
  };

  return (
    <input
      className={`cell-input ${error ? 'cell-invalid' : ''} ${className}`}
      style={{ textAlign: align }}
      value={editing ? draft : display}
      placeholder={editing || idlePlaceholder ? placeholder : undefined}
      aria-label={label}
      aria-invalid={error ? true : undefined}
      title={error ?? undefined}
      inputMode={inputMode}
      readOnly={readOnly}
      data-cell={cellId}
      autoComplete="off"
      spellCheck={false}
      onFocus={(e) => {
        if (readOnly) return;
        setEditing(true);
        setDraft(edit);
        setError(null);
        const target = e.currentTarget;
        requestAnimationFrame(() => target.select());
      }}
      onChange={(e) => {
        setDraft(e.target.value);
        if (error) setError(null);
      }}
      onBlur={() => {
        if (!skipBlurCommit.current && editing) commit();
        skipBlurCommit.current = false;
        setEditing(false);
      }}
      onKeyDown={onKeyDown}
    />
  );
}
