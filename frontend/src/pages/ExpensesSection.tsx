import { useCallback, useMemo, useRef, useState } from 'react';
import { newRequestId, useCategories, useCreateExpense, useDeleteExpense, useExpenses, useMatrix, usePatchExpense, useSaveLayout, useSetNote } from '../api/hooks';
import type { Column, ExpenseFilter, ExpenseList, ExpenseRow, LedgerTotals, Matrix, MatrixCell, MatrixDay } from '../api/types';
import { ColumnHeader } from '../components/ColumnEditor';
import { ColumnManager } from '../components/ColumnManager';
import { EditCell } from '../components/EditCell';
import { EntrySheet } from '../components/EntrySheet';
import { Icon } from '../components/Icon';
import { useQuickAdd } from '../components/QuickAdd';
import { ScopeChoice, Sheet } from '../components/Sheet';
import { toast } from '../components/Toast';
import { SectionTitle, useWorkspace } from '../components/Workspace';
import { parseAmounts, toExpression } from '../lib/amounts';
import { useSeriesColor } from '../lib/colors';
import { dayLabel, ymLabel } from '../lib/dates';
import { money } from '../lib/format';

const VIEW_KEY = 'expense-tracker.expense-view';

export function ExpensesSection() {
  const [view, setView] = useState<'matrix' | 'ledger'>(() => {
    try {
      return localStorage.getItem(VIEW_KEY) === 'ledger' ? 'ledger' : 'matrix';
    } catch {
      return 'matrix';
    }
  });
  const choose = (v: 'matrix' | 'ledger') => {
    setView(v);
    try {
      localStorage.setItem(VIEW_KEY, v);
    } catch {
      // Remembering the view is a convenience only.
    }
  };

  return (
    <div className="page">
      <SectionTitle
        actions={
          <div className="segmented" role="radiogroup" aria-label="View">
            <button type="button" role="radio" aria-checked={view === 'matrix'} className={view === 'matrix' ? 'on' : ''} onClick={() => choose('matrix')}>
              <Icon name="grid" size={14} /> Daily log
            </button>
            <button type="button" role="radio" aria-checked={view === 'ledger'} className={view === 'ledger' ? 'on' : ''} onClick={() => choose('ledger')}>
              <Icon name="bars" size={14} /> All entries
            </button>
          </div>
        }
      >
        Expenses
      </SectionTitle>
      {view === 'matrix' ? <MatrixView /> : <LedgerView />}
    </div>
  );
}

export function AdditionalSection() {
  return (
    <div className="page">
      <SectionTitle>Additional expenses</SectionTitle>
      <p className="hint">
        Unexpected or one-off spending, flagged on the entry. This is a filtered view of the same ledger: these amounts are already included in the
        month's totals and are never counted twice.
      </p>
      <LedgerView fixed={{ additional: true }} />
    </div>
  );
}

// ---------- Ledger (spreadsheet view) ----------

const SORTS = [
  { value: 'date', label: 'Date' },
  { value: 'amount', label: 'Amount' },
  { value: 'category', label: 'Category' },
  { value: 'description', label: 'Description' },
  { value: 'status', label: 'Status' },
];

function LedgerView({ fixed }: { fixed?: Partial<ExpenseFilter> }) {
  const { ym, me } = useWorkspace();
  const categories = useCategories();
  const [filter, setFilter] = useState<ExpenseFilter>({ sort: 'date', dir: 'asc' });
  const [showFilters, setShowFilters] = useState(false);
  const [editing, setEditing] = useState<ExpenseRow | 'new' | null>(null);
  const [managing, setManaging] = useState(false);
  const list = useExpenses(ym, { ...filter, ...fixed });
  const d = list.data;

  const set = (patch: Partial<ExpenseFilter>) => setFilter((f) => ({ ...f, ...patch }));
  const spending = (categories.data ?? []).filter((c) => c.type === 'Expense');

  return (
    <div className={list.isPlaceholderData ? 'is-refetching' : ''}>
      <div className="toolbar">
        <label className="search">
          <Icon name="target" size={14} />
          <span className="sr-only">Search</span>
          <input type="search" placeholder="Search entries" value={filter.search ?? ''} onChange={(e) => set({ search: e.target.value })} />
        </label>
        <button type="button" className={`btn btn-sm ${showFilters ? 'on' : ''}`} aria-expanded={showFilters} onClick={() => setShowFilters(!showFilters)}>
          <Icon name="sliders" size={14} /> Filters
        </button>
        <label className="sort">
          <span className="sr-only">Sort by</span>
          <select value={filter.sort} onChange={(e) => set({ sort: e.target.value })}>
            {SORTS.map((s) => (
              <option key={s.value} value={s.value}>
                Sort: {s.label}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className="btn btn-sm btn-icon" aria-label={filter.dir === 'asc' ? 'Ascending' : 'Descending'} onClick={() => set({ dir: filter.dir === 'asc' ? 'desc' : 'asc' })}>
          {filter.dir === 'asc' ? '↑' : '↓'}
        </button>
        <span className="spacer" />
        <button type="button" className="btn btn-sm" onClick={() => setManaging(true)} disabled={!d}>
          <Icon name="grid" size={14} /> Columns
        </button>
        <button type="button" className="btn btn-sm btn-primary" onClick={() => setEditing('new')}>
          <Icon name="plus" size={14} /> Row
        </button>
      </div>

      {showFilters && (
        <div className="filters">
          <label className="field">
            <span>Category</span>
            <select value={filter.categoryId ?? ''} onChange={(e) => set({ categoryId: Number(e.target.value) || undefined })}>
              <option value="">All</option>
              {spending.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>From</span>
            <input type="date" value={filter.from ?? ''} min={`${ym}-01`} onChange={(e) => set({ from: e.target.value || undefined })} />
          </label>
          <label className="field">
            <span>To</span>
            <input type="date" value={filter.to ?? ''} min={`${ym}-01`} onChange={(e) => set({ to: e.target.value || undefined })} />
          </label>
          <label className="field">
            <span>Status</span>
            <select value={filter.status ?? ''} onChange={(e) => set({ status: e.target.value as ExpenseFilter['status'] })}>
              <option value="">All</option>
              <option>Paid</option>
              <option>Planned</option>
            </select>
          </label>
          <label className="field">
            <span>Type</span>
            <select value={filter.kind ?? ''} onChange={(e) => set({ kind: e.target.value as ExpenseFilter['kind'] })}>
              <option value="">All</option>
              <option>Expense</option>
              <option>Refund</option>
              <option>Transfer</option>
            </select>
          </label>
          <label className="field">
            <span>Paid with</span>
            <select value={filter.paymentMethod ?? ''} onChange={(e) => set({ paymentMethod: e.target.value || undefined })}>
              <option value="">Any</option>
              {me.paymentMethods.map((m) => (
                <option key={m}>{m}</option>
              ))}
            </select>
          </label>
          <button type="button" className="btn btn-sm" onClick={() => setFilter({ sort: filter.sort, dir: filter.dir })}>
            Clear
          </button>
        </div>
      )}

      {list.isError && <p className="error-text">{list.error.message}</p>}
      {!d && !list.isError && <div className="loading">Loading…</div>}
      {d && (
        <>
          <TotalsBar d={d} />
          <LedgerTable d={d} onOpen={setEditing} />
          <LedgerCards d={d} onOpen={setEditing} />
          {managing && <ColumnManager ym={ym} columns={d.columns} layout={d.layout} onClose={() => setManaging(false)} />}
        </>
      )}
      {editing && <EntrySheet row={editing === 'new' ? null : editing} preset={{ isAdditional: fixed?.additional }} onClose={() => setEditing(null)} />}
    </div>
  );
}

function TotalsBar({ d }: { d: ExpenseList }) {
  const Totals = ({ t, label, tone }: { t: LedgerTotals; label: string; tone: string }) => (
    <div className={`totals totals-${tone}`}>
      <span className="totals-label">{label}</span>
      <span>
        <b>{money(t.net, { dash: false })}</b> net
      </span>
      <span className="muted small">
        {t.count} entr{t.count === 1 ? 'y' : 'ies'} · gross {money(t.gross)} · refunds {money(t.refunds)}
        {t.planned ? ` · planned ${money(t.planned)}` : ''}
        {t.transfers ? ` · transfers ${money(t.transfers)}` : ''}
      </span>
    </div>
  );
  return (
    <div className="totals-bar">
      {d.isFiltered && <Totals t={d.filtered} label="Filtered rows" tone="filtered" />}
      <Totals t={d.monthTotals} label={`Whole of ${ymLabel(d.month)}`} tone="month" />
    </div>
  );
}

function visibleColumns(d: ExpenseList): { column: Column; width?: number | null }[] {
  const byKey = new Map(d.columns.map((c) => [c.key, c]));
  return d.layout.columns.filter((l) => !l.hidden && byKey.has(l.key)).map((l) => ({ column: byKey.get(l.key)!, width: l.width }));
}

function LedgerTable({ d, onOpen }: { d: ExpenseList; onOpen: (r: ExpenseRow) => void }) {
  const cols = visibleColumns(d);
  return (
    <div className="table-scroll ledger-scroll hide-sm">
      <table className="data-table ledger-table">
        <thead>
          <tr>
            {cols.map(({ column, width }) => (
              <th key={column.key} scope="col" style={width ? { width, minWidth: width } : undefined} className={column.type === 'money' || column.type === 'number' || column.type === 'formula' ? 'num' : ''}>
                {column.name}
                {column.type === 'formula' && <span className="muted small"> ƒ</span>}
              </th>
            ))}
            <th scope="col">
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {d.rows.length === 0 && (
            <tr>
              <td colSpan={cols.length + 1} className="muted">
                No entries match.
              </td>
            </tr>
          )}
          {d.rows.map((r) => (
            <tr key={r.id} className={`${r.status === 'Planned' ? 'row-plan' : ''} ${r.kind !== 'Expense' ? `row-${r.kind.toLowerCase()}` : ''}`}>
              {cols.map(({ column }) => (
                <LedgerCell key={column.key} row={r} column={column} />
              ))}
              <td className="actions">
                <button type="button" className="btn-ghost btn-round" aria-label="Open entry" onClick={() => onOpen(r)}>
                  <Icon name="pencil" size={14} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function LedgerCell({ row, column }: { row: ExpenseRow; column: Column }) {
  const patch = usePatchExpense();
  const color = useSeriesColor();
  const save = (p: Parameters<typeof patch.mutate>[0]) => patch.mutate(p, { onSuccess: () => toast('Saved', 'success') });
  const base = { id: row.id, version: row.version };

  switch (column.key) {
    case 'date':
      return <td className="nowrap">{dayLabel(row.date).day}</td>;
    case 'category':
      return (
        <td className="nowrap">
          {row.kind !== 'Transfer' && <span className="swatch" style={{ background: color(row.categoryColor) }} aria-hidden="true" />}
          {row.kind === 'Transfer' ? `${row.accountName} → ${row.transferAccountName}` : row.categoryName}
        </td>
      );
    case 'amount':
      return (
        <td className={row.kind === 'Expense' && row.status === 'Paid' ? 'editable' : 'num'}>
          {row.kind === 'Expense' ? (
            <EditCell
              display={money(row.amount)}
              edit={String(row.amount)}
              parse={(t) => {
                const p = parseAmounts(t);
                return p.ok && p.amounts.length === 1 ? { ok: true, value: p.amounts[0] } : { ok: false, error: 'Enter one amount (or open the entry)' };
              }}
              isSame={(v) => v === row.amount}
              onCommit={(amount) => save({ ...base, amount })}
              label={`Amount of ${row.description || row.categoryName}`}
            />
          ) : row.kind === 'Refund' ? (
            `−${money(row.amount)}`
          ) : (
            money(row.amount)
          )}
        </td>
      );
    case 'description':
      return (
        <td className="editable">
          <EditCell
            display={row.description}
            edit={row.description}
            parse={(t) => ({ ok: true, value: t.trim() })}
            isSame={(t) => t === row.description}
            onCommit={(description) => save({ ...base, description })}
            label="Description"
            align="left"
            inputMode="text"
          />
        </td>
      );
    case 'kind':
      return <td>{row.kind === 'Expense' ? '' : <span className="badge">{row.kind}</span>}</td>;
    case 'status':
      return <td>{row.status === 'Planned' ? <span className="badge badge-plan">Planned</span> : <span className="muted small">Paid</span>}</td>;
    case 'method':
      return <td className="nowrap muted">{row.paymentMethod}</td>;
    case 'additional':
      return (
        <td className="center">
          {row.kind === 'Expense' && (
            <input type="checkbox" aria-label="Additional" checked={row.isAdditional} onChange={(e) => save({ ...base, isAdditional: e.target.checked })} />
          )}
        </td>
      );
    case 'recurring':
      return <td className="center">{row.isRecurring ? '↻' : ''}</td>;
  }

  if (column.type === 'formula') {
    const v = row.computed[column.key];
    return (
      <td className={`num calc ${v?.error ? 'cell-error' : ''}`} title={v?.error ? v.display ?? '' : undefined}>
        {v?.error ? v.error : formatCustom(column, v?.value)}
      </td>
    );
  }

  const value = row.custom[column.key];
  if (column.type === 'checkbox')
    return (
      <td className="center">
        <input type="checkbox" aria-label={column.name} checked={value === true} onChange={(e) => save({ ...base, custom: { [column.key]: e.target.checked } })} />
      </td>
    );
  if (column.type === 'dropdown')
    return (
      <td className="editable">
        <select className="cell-select" aria-label={column.name} value={typeof value === 'string' ? value : ''} onChange={(e) => save({ ...base, custom: { [column.key]: e.target.value || null } })}>
          <option value="">—</option>
          {column.options.map((o) => (
            <option key={o}>{o}</option>
          ))}
        </select>
      </td>
    );
  const numeric = column.type === 'money' || column.type === 'number';
  return (
    <td className="editable">
      <EditCell
        display={formatCustom(column, value)}
        edit={value == null ? '' : String(value)}
        parse={(t) => ({ ok: true, value: t.trim() === '' ? null : t.trim() })}
        isSame={(t) => (t ?? '') === (value == null ? '' : String(value))}
        onCommit={(v) => save({ ...base, custom: { [column.key]: v } })}
        label={column.name}
        align={numeric ? 'right' : 'left'}
        inputMode={numeric ? 'decimal' : 'text'}
        placeholder={column.type === 'date' ? 'YYYY-MM-DD' : undefined}
      />
    </td>
  );
}

function formatCustom(column: Column, value: unknown): string {
  if (value == null || value === '') return '';
  if (column.type === 'money') return money(Number(value));
  if (column.type === 'number' || (column.type === 'formula' && typeof value === 'number'))
    return Number(value).toLocaleString('en-US', { maximumFractionDigits: column.decimals });
  if (typeof value === 'boolean') return value ? 'TRUE' : 'FALSE';
  return String(value);
}

// Phones: one card per entry; tap to edit.
function LedgerCards({ d, onOpen }: { d: ExpenseList; onOpen: (r: ExpenseRow) => void }) {
  const color = useSeriesColor();
  const custom = d.columns.filter((c) => !c.builtIn && !c.isArchived && !d.layout.columns.find((l) => l.key === c.key)?.hidden);
  return (
    <ul className="entry-cards show-sm">
      {d.rows.length === 0 && <li className="muted">No entries match.</li>}
      {d.rows.map((r) => (
        <li key={r.id}>
          <button type="button" className={`entry-card ${r.status === 'Planned' ? 'row-plan' : ''}`} onClick={() => onOpen(r)}>
            <span className="entry-dot" style={{ background: color(r.categoryColor) }} aria-hidden="true" />
            <span className="entry-main">
              <span className="entry-title">{r.description || r.categoryName || r.kind}</span>
              <span className="muted small">
                {dayLabel(r.date).day} · {r.kind === 'Transfer' ? 'Transfer' : r.categoryName}
                {r.paymentMethod && ` · ${r.paymentMethod}`}
                {custom.map((c) => {
                  const v = c.type === 'formula' ? r.computed[c.key]?.error ?? r.computed[c.key]?.value : r.custom[c.key];
                  return v == null || v === '' ? null : ` · ${c.name}: ${c.type === 'formula' && r.computed[c.key]?.error ? v : formatCustom(c, v)}`;
                })}
              </span>
              <span className="entry-badges">
                {r.status === 'Planned' && <span className="badge badge-plan">Planned</span>}
                {r.isAdditional && <span className="badge">Additional</span>}
                {r.isRecurring && <span className="badge">Recurring</span>}
                {r.kind === 'Refund' && <span className="badge badge-good">Refund</span>}
              </span>
            </span>
            <span className={`entry-amount ${r.kind === 'Refund' ? 'positive' : ''}`}>
              {r.kind === 'Refund' ? '−' : ''}
              {money(r.amount)}
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}

// ---------- Daily matrix ----------

type Move = 'down' | 'up' | 'left' | 'right';

function MatrixView() {
  const { ym } = useWorkspace();
  const matrix = useMatrix(ym);
  const m = matrix.data;
  const tableRef = useRef<HTMLTableElement>(null);
  const [cell, setCell] = useState<{ day: MatrixDay; categoryId: number } | null>(null);
  const [layoutOpen, setLayoutOpen] = useState(false);

  const visible = useMemo(() => (m ? m.categories.filter((c) => !c.hidden) : []), [m]);
  const focusCell = useCallback(
    (row: number, col: number, move: Move) => {
      const rows = m?.days.length ?? 0;
      const cols = visible.length + 1;
      const r = move === 'down' ? Math.min(rows - 1, row + 1) : move === 'up' ? Math.max(0, row - 1) : row;
      const c = move === 'right' ? Math.min(cols - 1, col + 1) : move === 'left' ? Math.max(0, col - 1) : col;
      const el = tableRef.current?.querySelector<HTMLElement>(`[data-cell="${r}:${c}"]`);
      el?.focus();
    },
    [m?.days.length, visible.length],
  );

  if (matrix.isError) return <p className="error-text">{matrix.error.message}</p>;
  if (!m) return <div className="loading">Loading…</div>;

  return (
    <div className={matrix.isPlaceholderData ? 'is-refetching' : ''}>
      <div className="toolbar">
        <p className="hint grow">
          Type an amount in a yellow cell; <code>100+50</code> saves two entries. A cell with several entries opens them so each one is edited on its own.
          Planned amounts are shown in grey and not counted.
        </p>
        <button type="button" className="btn btn-sm" onClick={() => setLayoutOpen(true)}>
          <Icon name="grid" size={14} /> Columns
        </button>
      </div>
      <MatrixTable m={m} visible={visible} tableRef={tableRef} onNavigate={focusCell} onOpenCell={(day, categoryId) => setCell({ day, categoryId })} />
      {cell && <CellSheet m={m} day={m.days.find((d) => d.date === cell.day.date) ?? cell.day} categoryId={cell.categoryId} onClose={() => setCell(null)} />}
      {layoutOpen && <DailyLayoutSheet m={m} onClose={() => setLayoutOpen(false)} />}
    </div>
  );
}

function MatrixTable({
  m,
  visible,
  tableRef,
  onNavigate,
  onOpenCell,
}: {
  m: Matrix;
  visible: Matrix['categories'];
  tableRef: React.RefObject<HTMLTableElement | null>;
  onNavigate: (row: number, col: number, move: Move) => void;
  onOpenCell: (day: MatrixDay, categoryId: number) => void;
}) {
  const color = useSeriesColor();
  const create = useCreateExpense();
  const patch = usePatchExpense();
  const remove = useDeleteExpense();
  const setNote = useSetNote();
  const quick = useQuickAdd();

  const commitCell = async (day: MatrixDay, categoryId: number, cell: MatrixCell | undefined, amounts: number[]) => {
    const status = day.date > m.today ? 'Planned' : 'Paid';
    const single = cell?.entries.length === 1 ? cell.entries[0] : null;
    try {
      if (single) {
        if (amounts.length === 0) {
          if (!confirm(`Delete the ${money(single.amount)} entry?`)) return;
          await remove.mutateAsync({ id: single.id, version: single.version });
          return;
        }
        await patch.mutateAsync({ id: single.id, version: single.version, amount: amounts[0] });
      }
      // Extra amounts ("=500+100") are added as separate entries, never merged into one.
      for (const amount of single ? amounts.slice(1) : amounts) {
        await create.mutateAsync({ clientRequestId: newRequestId(), date: day.date, amount, kind: 'Expense', status, categoryId, description: '', paymentMethod: 'Cash', isAdditional: false });
      }
      toast('Saved', 'success');
    } catch (err) {
      toast(err instanceof Error ? err.message : 'Could not save', 'error');
    }
  };

  return (
    <div className="table-scroll log-scroll">
      <table className="data-table log-table" ref={tableRef}>
        <thead>
          <tr>
            <th scope="col" className="sticky-col">Date</th>
            {visible.map((c) => (
              <th key={c.id} scope="col" className="num col-th" style={{ ['--col' as string]: color(c.color), ...(c.width ? { minWidth: c.width } : {}) }}>
                <ColumnHeader id={c.id} name={c.name} swatch={color(c.color)} />
              </th>
            ))}
            <th scope="col" className="num">Daily total</th>
            <th scope="col" className="remarks-col">Remarks</th>
          </tr>
        </thead>
        <tbody>
          {m.days.map((day, r) => {
            const { day: label, weekday } = dayLabel(day.date);
            const isToday = day.date === m.today;
            return (
              <tr key={day.date} className={isToday ? 'row-today' : day.date > m.today ? 'row-future' : ''} aria-current={isToday ? 'date' : undefined}>
                <th scope="row" className="sticky-col date-cell">
                  <button type="button" className="date-btn" onClick={() => quick.open({ date: day.date })} title="Add an expense on this day">
                    <span className="date-day">{label}</span>
                    <span className="date-weekday">{weekday}</span>
                  </button>
                </th>
                {visible.map((c, col) => {
                  const cell = day.cells[c.id];
                  const entries = cell?.entries ?? [];
                  if (entries.length > 1)
                    return (
                      <td key={c.id} className="multi-cell">
                        <button type="button" className="cell-button" data-cell={`${r}:${col}`} onClick={() => onOpenCell(day, c.id)} aria-label={`${entries.length} entries in ${c.name} on ${label}`}>
                          {cell!.net ? money(cell!.net) : ''}
                          {cell!.planned ? <span className="planned-amount"> {money(cell!.planned)}</span> : null}
                          <span className="entry-count">{entries.length}</span>
                        </button>
                      </td>
                    );
                  const single = entries[0];
                  const planned = single?.status === 'Planned';
                  return (
                    <td key={c.id} className={`editable ${planned ? 'cell-planned' : ''}`}>
                      <EditCell
                        display={single ? money(single.amount) : ''}
                        edit={single ? toExpression([single.amount]) : ''}
                        parse={(t) => {
                          const p = parseAmounts(t);
                          return p.ok ? { ok: true, value: p.amounts } : p;
                        }}
                        isSame={(v) => (single ? v.length === 1 && v[0] === single.amount : v.length === 0)}
                        onCommit={(amounts) => commitCell(day, c.id, cell, amounts)}
                        cellId={`${r}:${col}`}
                        label={`${c.name} on ${weekday} ${label}`}
                        onNavigate={(move) => onNavigate(r, col, move)}
                        placeholder="=100+50"
                      />
                    </td>
                  );
                })}
                <td className="num calc total-cell">{money(day.net)}</td>
                <td className="editable remarks-col">
                  <EditCell
                    display={day.note}
                    edit={day.note}
                    parse={(t) => ({ ok: true, value: t.trim() })}
                    isSame={(t) => t === day.note}
                    onCommit={(text) => setNote.mutate({ date: day.date, text })}
                    cellId={`${r}:${visible.length}`}
                    label={`Remarks for ${weekday} ${label}`}
                    align="left"
                    inputMode="text"
                    onNavigate={(move) => onNavigate(r, visible.length, move)}
                  />
                </td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" className="sticky-col">Total</th>
            {visible.map((c) => (
              <td key={c.id} className="num">
                {money(m.columnTotals[c.id] ?? 0)}
              </td>
            ))}
            <td className="num grand-total">{money(m.net)}</td>
            <td />
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

// Several entries in one cell: each is edited separately, never replaced by a total.
function CellSheet({ m, day, categoryId, onClose }: { m: Matrix; day: MatrixDay; categoryId: number; onClose: () => void }) {
  const category = m.categories.find((c) => c.id === categoryId);
  const entries = day.cells[categoryId]?.entries ?? [];
  const list = useExpenses(m.month, { from: day.date, to: day.date, categoryId });
  const [editing, setEditing] = useState<ExpenseRow | 'new' | null>(null);

  if (editing) return <EntrySheet row={editing === 'new' ? null : editing} preset={{ date: day.date, categoryId }} onClose={onClose} />;
  return (
    <Sheet title={`${category?.name ?? 'Entries'} · ${dayLabel(day.date).day}`} onClose={onClose}>
      <ul className="entry-list">
        {entries.map((e) => {
          const row = list.data?.rows.find((r) => r.id === e.id);
          return (
            <li key={e.id}>
              <span className="entry-name">
                {e.description || 'No description'}
                <span className="muted small">
                  {e.paymentMethod && ` · ${e.paymentMethod}`}
                  {e.status === 'Planned' && ' · planned'}
                  {e.kind === 'Refund' && ' · refund'}
                </span>
              </span>
              <span className="num">
                {e.kind === 'Refund' ? '−' : ''}
                {money(e.amount)}
              </span>
              <button type="button" className="btn-ghost btn-round" aria-label="Edit entry" disabled={!row} onClick={() => row && setEditing(row)}>
                <Icon name="pencil" size={14} />
              </button>
            </li>
          );
        })}
      </ul>
      <button type="button" className="btn btn-primary btn-block" onClick={() => setEditing('new')}>
        <Icon name="plus" size={14} /> Add another entry
      </button>
    </Sheet>
  );
}

function DailyLayoutSheet({ m, onClose }: { m: Matrix; onClose: () => void }) {
  const save = useSaveLayout();
  const [rows, setRows] = useState(m.categories.map((c) => ({ key: `cat:${c.id}`, name: c.name, hidden: c.hidden, width: c.width })));
  const [scope, setScope] = useState<'month' | 'future'>(m.layoutSource === 'month' ? 'month' : 'future');
  const move = (i: number, d: number) => {
    const j = i + d;
    if (j < 0 || j >= rows.length) return;
    const next = [...rows];
    [next[i], next[j]] = [next[j], next[i]];
    setRows(next);
  };
  return (
    <Sheet title="Daily log columns" onClose={onClose}>
      <div className="form-stack">
        <p className="muted small">Hiding a column only hides it here; its entries and totals stay. Rename or archive categories by clicking a column header.</p>
        <ul className="column-list">
          {rows.map((r, i) => (
            <li key={r.key} className={r.hidden ? 'archived' : ''}>
              <div className="column-order">
                <button type="button" className="btn-ghost" aria-label={`Move ${r.name} left`} disabled={i === 0} onClick={() => move(i, -1)}>↑</button>
                <button type="button" className="btn-ghost" aria-label={`Move ${r.name} right`} disabled={i === rows.length - 1} onClick={() => move(i, 1)}>↓</button>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={!r.hidden} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, hidden: !e.target.checked } : x)))} />
                <span>{r.name}</span>
              </label>
              <label className="width-input">
                <span className="sr-only">Width</span>
                <input type="number" min={40} max={400} step={10} placeholder="auto" value={r.width ?? ''} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, width: e.target.value ? Number(e.target.value) : null } : x)))} />
                px
              </label>
            </li>
          ))}
        </ul>
        <ScopeChoice value={scope} onChange={setScope} month={ymLabel(m.month)} />
        <button
          type="button"
          className="btn btn-primary"
          onClick={() =>
            save.mutate(
              { table: 'daily', ym: m.month, scope, columns: rows.map(({ key, hidden, width }) => ({ key, hidden, width })) },
              { onSuccess: () => (toast('Columns saved', 'success'), onClose()) },
            )
          }
        >
          Save
        </button>
      </div>
    </Sheet>
  );
}
