import { useEffect, useState, type FormEvent } from 'react';
import { checkFormula, useRemoveColumn, useRestoreColumn, useSaveColumn, useSaveLayout } from '../api/hooks';
import { ApiError } from '../api/client';
import type { Column, ColumnType, Layout, LayoutColumn } from '../api/types';
import { ymLabel } from '../lib/dates';
import { moveItem } from '../lib/reorder';
import { checkName, checkOptions, firstError, LIMITS } from '../lib/validate';
import { useDragReorder } from './Drag';
import { Icon } from './Icon';
import { ScopeChoice, Sheet } from './Sheet';
import { toast } from './Toast';

const TYPES: { value: ColumnType; label: string }[] = [
  { value: 'text', label: 'Text' },
  { value: 'money', label: 'Money' },
  { value: 'number', label: 'Number' },
  { value: 'date', label: 'Date' },
  { value: 'dropdown', label: 'Dropdown' },
  { value: 'checkbox', label: 'Checkbox' },
  { value: 'formula', label: 'Calculated (formula)' },
];

// Order, width and visibility for the ledger, plus adding, renaming and removing custom columns.
export function ColumnManager({ ym, columns, layout, onClose }: { ym: string; columns: Column[]; layout: Layout; onClose: () => void }) {
  const saveLayout = useSaveLayout();
  const restore = useRestoreColumn();
  const [rows, setRows] = useState<LayoutColumn[]>(layout.columns);
  const [scope, setScope] = useState<'month' | 'future'>(layout.source === 'month' ? 'month' : 'future');
  const [editing, setEditing] = useState<Column | 'new' | null>(null);
  const byKey = new Map(columns.map((c) => [c.key, c]));

  useEffect(() => setRows(layout.columns), [layout]);

  const move = (i: number, d: number) => {
    const j = i + d;
    if (j < 0 || j >= rows.length) return;
    const next = [...rows];
    [next[i], next[j]] = [next[j], next[i]];
    setRows(next);
  };

  const drag = useDragReorder((from, to) => setRows((list) => moveItem(list, from, to)));

  const save = () =>
    saveLayout.mutate(
      { table: 'expenses', ym, scope, columns: rows },
      { onSuccess: () => (toast(scope === 'month' ? `Layout saved for ${ymLabel(ym)}` : `Layout saved from ${ymLabel(ym)} on`, 'success'), onClose()) },
    );

  const archived = columns.filter((c) => !c.builtIn && c.isArchived);

  return (
    <Sheet title="Columns" onClose={onClose} wide>
      <div className="form-stack">
        <p className="muted small">
          Showing the {layout.source === 'month' ? `layout saved for ${ymLabel(ym)} only` : layout.source === 'template' ? `layout in use since ${ymLabel(layout.sourceMonth ?? ym)}` : 'default layout'}.
          Date, Category, Amount and Type can be hidden but always stay in the records.
        </p>
        <ul className="column-list" ref={drag.listRef}>
          {rows.map((r, i) => {
            const c = byKey.get(r.key);
            if (!c) return null;
            return (
              <li key={r.key} data-index={i} className={`${r.hidden ? 'archived' : ''} ${drag.dragging === i ? 'is-dragged' : ''}`}>
                <div className="column-order">
                  {drag.grip(i, c.name)}
                  <button type="button" className="btn-ghost" aria-label={`Move ${c.name} up`} disabled={i === 0} onClick={() => move(i, -1)}>
                    ↑
                  </button>
                  <button type="button" className="btn-ghost" aria-label={`Move ${c.name} down`} disabled={i === rows.length - 1} onClick={() => move(i, 1)}>
                    ↓
                  </button>
                </div>
                <label className="toggle">
                  <input type="checkbox" checked={!r.hidden} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, hidden: !e.target.checked } : x)))} />
                  <span className="column-name-label">{c.name}</span>
                </label>
                <span className="badge">{TYPES.find((t) => t.value === c.type)?.label ?? c.type}</span>
                {c.formulaError && <span className="badge badge-warn" title={c.formulaError}>formula error</span>}
                <label className="width-input">
                  <span className="sr-only">Width of {c.name}</span>
                  <input
                    type="number"
                    min={40}
                    max={800}
                    step={10}
                    placeholder="auto"
                    value={r.width ?? ''}
                    onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, width: e.target.value ? Number(e.target.value) : null } : x)))}
                  />
                  px
                </label>
                {!c.builtIn && (
                  <button type="button" className="btn-ghost" onClick={() => setEditing(c)}>
                    <Icon name="pencil" size={13} /> Edit
                  </button>
                )}
              </li>
            );
          })}
        </ul>
        <button type="button" className="btn btn-sm" onClick={() => setEditing('new')}>
          <Icon name="plus" size={14} /> Add column
        </button>

        {archived.length > 0 && (
          <div>
            <h3 className="mini-head">Archived columns (values kept)</h3>
            <ul className="plain-list">
              {archived.map((c) => (
                <li key={c.key}>
                  <span>{c.name}</span>
                  <button type="button" className="btn-ghost" onClick={() => c.id && restore.mutate(c.id)}>
                    Restore
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}

        <ScopeChoice value={scope} onChange={setScope} month={ymLabel(ym)} />
        <div className="form-actions">
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button type="button" className="btn btn-primary" disabled={saveLayout.isPending} onClick={save}>
            Save layout
          </button>
        </div>
      </div>
      {editing && <ColumnDialog column={editing === 'new' ? null : editing} columns={columns} onClose={() => setEditing(null)} />}
    </Sheet>
  );
}

function ColumnDialog({ column, columns, onClose }: { column: Column | null; columns: Column[]; onClose: () => void }) {
  const save = useSaveColumn();
  const remove = useRemoveColumn();
  const [name, setName] = useState(column?.name ?? '');
  const [type, setType] = useState<ColumnType>(column?.type ?? 'text');
  const [options, setOptions] = useState((column?.options ?? []).join(', '));
  const [formula, setFormula] = useState(column?.formula ?? '');
  const [decimals, setDecimals] = useState(column?.decimals ?? 2);
  const [check, setCheck] = useState<{ ok: boolean; error: string | null } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [impact, setImpact] = useState<{ valueCount: number; dependentColumns: string[] } | null>(null);

  // Live formula check against the server's parser (the same one that evaluates it).
  useEffect(() => {
    if (type !== 'formula' || !formula.trim()) {
      setCheck(null);
      return;
    }
    const t = window.setTimeout(() => {
      checkFormula(formula, column?.id).then(setCheck, () => setCheck(null));
    }, 300);
    return () => window.clearTimeout(t);
  }, [formula, type, column?.id]);

  // The server's rules, checked as you type: a unique name (built-in columns count too), real options, a formula.
  const nameCheck = checkName(name, LIMITS.columnName, columns.filter((c) => c.key !== column?.key).map((c) => c.name), 'column');
  const optionList = options.split(',');
  const optionsCheck = type === 'dropdown' ? checkOptions(optionList) : null;
  const problem = firstError(
    nameCheck,
    /[[\]]/.test(name) && "Column names can't contain [ or ]",
    optionsCheck,
    type === 'formula' && !formula.trim() && 'Enter a formula, e.g. [Amount] * 0.1',
    !Number.isInteger(decimals) || decimals < 0 || decimals > 6 ? 'Decimals must be between 0 and 6' : null,
  );
  const nameError = name.trim() || column ? firstError(nameCheck, /[[\]]/.test(name) && "Column names can't contain [ or ]") : null;
  const optionsError = optionsCheck && options.trim() ? firstError(optionsCheck) : null;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (problem) return setError(problem);
    try {
      await save.mutateAsync({
        id: column?.id,
        name: nameCheck.ok ? nameCheck.value : name,
        type,
        options: optionsCheck?.ok ? optionsCheck.value : optionList.map((o) => o.trim()).filter(Boolean),
        formula: type === 'formula' ? formula : null,
        decimals,
        version: column?.version,
      });
      toast(column ? 'Column saved' : 'Column added', 'success');
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save');
    }
  };

  const doRemove = async (mode?: 'archive' | 'delete') => {
    if (!column?.id) return;
    try {
      await remove.mutateAsync({ id: column.id, mode });
      toast(mode === 'archive' ? 'Column archived; its values are kept' : 'Column removed', 'success');
      onClose();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setImpact((err.detail as typeof impact) ?? { valueCount: 0, dependentColumns: [] });
      } else setError(err instanceof Error ? err.message : 'Could not remove');
    }
  };

  const names = columns.filter((c) => c.key !== column?.key && !c.isArchived).map((c) => `[${c.name}]`);

  return (
    <Sheet title={column ? `Edit “${column.name}”` : 'New column'} onClose={onClose}>
      <form className="form-stack" onSubmit={submit}>
        <label className="field">
          <span>Name</span>
          <input
            value={name}
            maxLength={LIMITS.columnName}
            aria-invalid={nameError ? true : undefined}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Shop, Receipt no., Tip"
          />
          {nameError && <small className="error-text">{nameError}</small>}
        </label>
        <label className="field">
          <span>Type</span>
          <select value={type} disabled={!!column && (column.type === 'formula') !== (type === 'formula') && false} onChange={(e) => setType(e.target.value as ColumnType)}>
            {TYPES.filter((t) => !column || (column.type === 'formula') === (t.value === 'formula')).map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </label>
        {type === 'dropdown' && (
          <label className="field">
            <span>Options (comma-separated)</span>
            <input value={options} aria-invalid={optionsError ? true : undefined} onChange={(e) => setOptions(e.target.value)} placeholder="Daraz, Imtiaz, Local shop" />
            {optionsError && <small className="error-text">{optionsError}</small>}
          </label>
        )}
        {(type === 'number' || type === 'formula') && (
          <label className="field">
            <span>Decimals shown</span>
            <input type="number" min={0} max={6} value={decimals} onChange={(e) => setDecimals(Number(e.target.value))} />
          </label>
        )}
        {type === 'formula' && (
          <label className="field">
            <span>Formula</span>
            <textarea rows={3} value={formula} onChange={(e) => setFormula(e.target.value)} placeholder="[Amount] * 0.1" spellCheck={false} />
            <small className="muted">
              Columns: {names.join(' ')}. Functions: SUM, AVERAGE, MIN, MAX, COUNT, IF, ROUND, ABS, SUMIF, SUMIFS. Calculated columns are for display; they never change your totals.
            </small>
            {check && (check.ok ? <small className="ok-text">✓ Formula is valid</small> : <small className="error-text">{check.error}</small>)}
          </label>
        )}
        {error && <p className="error-text" role="alert">{error}</p>}
        {impact && (
          <div className="warning-box" role="alert">
            <p>
              <b>This column has data.</b> {impact.valueCount} entr{impact.valueCount === 1 ? 'y has' : 'ies have'} a value
              {impact.dependentColumns.length > 0 && `, and ${impact.dependentColumns.join(', ')} use${impact.dependentColumns.length === 1 ? 's' : ''} it in a formula`}.
            </p>
            <p className="muted small">Archive hides it everywhere and keeps every value (formulas keep working). Delete removes the values; formulas using it will show #REF!.</p>
            <div className="btn-row">
              <button type="button" className="btn" onClick={() => doRemove('archive')}>
                Archive (keep values)
              </button>
              <button type="button" className="btn btn-danger-ghost" onClick={() => confirm('Delete the column and its values?') && doRemove('delete')}>
                Delete values
              </button>
            </div>
          </div>
        )}
        <div className="form-actions">
          {column && !impact && (
            <button type="button" className="btn btn-danger-ghost" onClick={() => doRemove()}>
              Remove
            </button>
          )}
          <span className="spacer" />
          <button className="btn btn-primary" disabled={save.isPending || !!problem || (type === 'formula' && check?.ok === false)} title={problem ?? undefined}>
            {column ? 'Save' : 'Add column'}
          </button>
        </div>
      </form>
    </Sheet>
  );
}
