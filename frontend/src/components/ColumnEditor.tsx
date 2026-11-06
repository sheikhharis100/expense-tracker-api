import { createContext, useContext, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useCategories, useDeleteCategory, useReorderCategories, useSaveCategory } from '../api/hooks';
import type { Category, CategoryType } from '../api/types';
import { PALETTE, useSeriesColor } from '../lib/colors';
import { Icon } from './Icon';
import { toast } from './Toast';

type Target = { id: number } | { create: CategoryType };

interface EditorApi {
  open: (target: Target) => void;
}

const EditorContext = createContext<EditorApi>({ open: () => {} });

export const useColumnEditor = () => useContext(EditorContext);

// One editor for every column header in the app: Daily log, Every month, This month and Settings.
export function ColumnEditorProvider({ children }: { children: ReactNode }) {
  const [target, setTarget] = useState<Target | null>(null);
  return (
    <EditorContext.Provider value={{ open: setTarget }}>
      {children}
      {target && <ColumnEditorDialog key={JSON.stringify(target)} target={target} onClose={() => setTarget(null)} />}
    </EditorContext.Provider>
  );
}

// Moves a column one place left/right among columns of the same type, keeping the other type's order.
export function useMoveCategory() {
  const reorder = useReorderCategories();
  return (all: Category[], id: number, delta: number) => {
    const target = all.find((c) => c.id === id);
    if (!target) return;
    const sorted = (type: CategoryType) =>
      all.filter((c) => c.type === type).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
    const same = sorted(target.type).map((c) => c.id);
    const index = same.indexOf(id);
    const next = index + delta;
    if (next < 0 || next >= same.length) return;
    [same[index], same[next]] = [same[next], same[index]];
    const others = sorted(target.type === 'Expense' ? 'Income' : 'Expense').map((c) => c.id);
    reorder.mutate(target.type === 'Expense' ? [...same, ...others] : [...others, ...same]);
  };
}

function nextFreeColor(all: Category[]): string {
  const used = new Set(all.map((c) => c.color.toLowerCase()));
  return PALETTE.find((p) => !used.has(p.light))?.light ?? PALETTE[all.length % PALETTE.length].light;
}

function ColumnEditorDialog({ target, onClose }: { target: Target; onClose: () => void }) {
  const categories = useCategories();
  const save = useSaveCategory();
  const remove = useDeleteCategory();
  const move = useMoveCategory();
  const color = useSeriesColor();
  const nameRef = useRef<HTMLInputElement>(null);

  const all = categories.data ?? [];
  const existing = 'id' in target ? all.find((c) => c.id === target.id) : undefined;
  const isNew = !('id' in target);

  const [name, setName] = useState(existing?.name ?? '');
  const [type, setType] = useState<CategoryType>(existing?.type ?? ('create' in target ? target.create : 'Expense'));
  const [hex, setHex] = useState(existing?.color ?? nextFreeColor(all));
  const [group, setGroup] = useState(existing?.groupName ?? '');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    nameRef.current?.focus();
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  if (!isNew && !existing) {
    return categories.isLoading ? null : (
      <Modal title="Column not found" onClose={onClose}>
        <p className="muted">This column was removed, maybe from another device.</p>
      </Modal>
    );
  }

  const canChangeType = isNew || existing?.transactionCount === 0;
  const groups = [...new Set(all.map((c) => c.groupName).filter((g): g is string => !!g))];
  const siblings = all.filter((c) => c.type === type).sort((a, b) => a.sortOrder - b.sortOrder);
  const position = existing ? siblings.findIndex((c) => c.id === existing.id) : -1;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return setError('Give the column a name');
    save.mutate(
      {
        id: existing?.id,
        name: trimmed,
        type,
        color: hex,
        groupName: type === 'Expense' ? group.trim() || null : null,
        isArchived: existing?.isArchived ?? false,
      },
      {
        onSuccess: () => {
          toast(isNew ? `Added “${trimmed}”` : `Saved “${trimmed}”`, 'success');
          onClose();
        },
        onError: (err) => setError(err.message),
      },
    );
  };

  const archive = () => {
    if (!existing) return;
    save.mutate(
      { ...existing, isArchived: !existing.isArchived },
      { onSuccess: () => (toast(existing.isArchived ? `Restored “${existing.name}”` : `Archived “${existing.name}”`, 'success'), onClose()) },
    );
  };

  const del = () => {
    if (!existing || !confirm(`Delete “${existing.name}”? This can't be undone.`)) return;
    remove.mutate(existing.id, { onSuccess: () => (toast(`Deleted “${existing.name}”`, 'success'), onClose()) });
  };

  const title = isNew ? (type === 'Expense' ? 'New spending category' : 'New income category') : 'Edit category';

  return (
    <Modal title={title} onClose={onClose}>
      <form className="editor" onSubmit={submit}>
        <div className="editor-preview" style={{ ['--chip' as string]: color(hex) }}>
          <span className="swatch swatch-lg" style={{ background: color(hex) }} aria-hidden="true" />
          <span>{name.trim() || 'Column name'}</span>
          {existing && <span className="muted small">{existing.transactionCount} {existing.transactionCount === 1 ? 'entry' : 'entries'}</span>}
        </div>

        <label className="field">
          <span>Name</span>
          <input ref={nameRef} value={name} maxLength={100} onChange={(e) => setName(e.target.value)} placeholder="e.g. Groceries" />
        </label>

        <fieldset className="field">
          <legend>Type</legend>
          <div className="segmented" role="radiogroup">
            {(['Expense', 'Income'] as const).map((t) => (
              <button
                key={t}
                type="button"
                role="radio"
                aria-checked={type === t}
                className={type === t ? 'on' : ''}
                disabled={!canChangeType}
                onClick={() => setType(t)}
              >
                {t === 'Expense' ? 'Spending' : 'Income'}
              </button>
            ))}
          </div>
          {!canChangeType && <p className="muted small">Categories with entries keep their type. Budgets are set per month under Budgets & Recurring.</p>}
        </fieldset>

        <fieldset className="field">
          <legend>Colour</legend>
          <div className="swatches">
            {PALETTE.map((p) => (
              <button
                key={p.light}
                type="button"
                className={`swatch-btn ${hex.toLowerCase() === p.light ? 'on' : ''}`}
                style={{ background: color(p.light) }}
                aria-label={p.name}
                aria-pressed={hex.toLowerCase() === p.light}
                onClick={() => setHex(p.light)}
              />
            ))}
            <label className={`swatch-btn swatch-custom ${PALETTE.some((p) => p.light === hex.toLowerCase()) ? '' : 'on'}`} title="Custom colour">
              <input type="color" value={hex} onChange={(e) => setHex(e.target.value)} aria-label="Custom colour" />
              <Icon name="plus" size={14} />
            </label>
          </div>
        </fieldset>

        {type === 'Expense' && (
          <div className="editor-row">
            <label className="field">
              <span>Group (optional, for a combined total)</span>
              <input value={group} maxLength={50} list="column-groups" onChange={(e) => setGroup(e.target.value)} placeholder="e.g. Travel" />
              <datalist id="column-groups">
                {groups.map((g) => (
                  <option key={g} value={g} />
                ))}
              </datalist>
            </label>
          </div>
        )}

        {existing && (
          <div className="editor-move">
            <span className="muted small">Position</span>
            <button type="button" className="btn btn-sm" disabled={position <= 0} onClick={() => move(all, existing.id, -1)}>
              <Icon name="left" size={14} /> Move left
            </button>
            <button type="button" className="btn btn-sm" disabled={position >= siblings.length - 1} onClick={() => move(all, existing.id, 1)}>
              Move right <Icon name="right" size={14} />
            </button>
          </div>
        )}

        {error && <p className="error-text">{error}</p>}

        <div className="editor-actions">
          {existing && existing.transactionCount === 0 && (
            <button type="button" className="btn btn-danger-ghost" onClick={del}>
              Delete
            </button>
          )}
          {existing && (
            <button type="button" className="btn" onClick={archive}>
              {existing.isArchived ? 'Restore' : 'Archive'}
            </button>
          )}
          <span className="spacer" />
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={save.isPending}>
            {isNew ? 'Add category' : 'Save'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-head">
          <h2>{title}</h2>
          <button type="button" className="btn-ghost btn-round" aria-label="Close" onClick={onClose}>
            <Icon name="x" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

// A column header that opens the editor; used in every table that shows categories.
export function ColumnHeader({ id, name, swatch }: { id: number; name: string; swatch: string }) {
  const { open } = useColumnEditor();
  return (
    <button type="button" className="col-head" onClick={() => open({ id })} title={`Edit “${name}”`}>
      <span className="swatch" style={{ background: swatch }} aria-hidden="true" />
      <span className="col-head-name">{name}</span>
      <Icon name="pencil" size={12} className="col-head-edit" />
    </button>
  );
}
