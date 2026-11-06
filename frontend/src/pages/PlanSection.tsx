import { useEffect, useState, type FormEvent } from 'react';
import {
  useBudgets,
  useCategories,
  useCopyMonth,
  useMonthSettings,
  useOccurrenceAction,
  useRecurring,
  useSaveMonthSettings,
  useSaveRecurring,
  useSetBudget,
  useStopRecurring,
  useUpdateOccurrence,
} from '../api/hooks';
import type { BudgetLine, Occurrence, RecurringItem } from '../api/types';
import { Icon } from '../components/Icon';
import { Meter } from '../components/Meter';
import { ScopeChoice, Sheet } from '../components/Sheet';
import { toast } from '../components/Toast';
import { SectionTitle, useWorkspace } from '../components/Workspace';
import { parseAmounts, parseSigned } from '../lib/amounts';
import { useSeriesColor } from '../lib/colors';
import { dayLabel, shiftKey, ymLabel } from '../lib/dates';
import { money, signed } from '../lib/format';

export function PlanSection() {
  return (
    <div className="page">
      <SectionTitle>Budgets & Recurring</SectionTitle>
      <Budgets />
      <Recurring />
      <MonthSettingsCard />
    </div>
  );
}

// ---------- Budgets ----------

function Budgets() {
  const { ym } = useWorkspace();
  const budgets = useBudgets(ym);
  const color = useSeriesColor();
  const [editing, setEditing] = useState<BudgetLine | null>(null);
  const b = budgets.data;

  return (
    <section className={`card ${budgets.isPlaceholderData ? 'is-refetching' : ''}`}>
      <div className="card-head">
        <h2>
          <Icon name="target" className="card-icon" /> Budgets for {ymLabel(ym)}
        </h2>
        <span className="muted small">{b?.totalBudget == null ? 'Not set' : `${money(b.totalBudget)} in total`}</span>
      </div>
      {b?.coverageWarning && (
        <p className="warning-text">
          <Icon name="alert" size={14} /> {money(b.unbudgetedSpending)} spent without a budget ({b.unbudgetedCategories.join(', ')}).
        </p>
      )}
      {b && (
        <div className="table-scroll">
          <table className="data-table budget-table">
            <thead>
              <tr>
                <th scope="col">Category</th>
                <th scope="col" className="num">Budget</th>
                <th scope="col" className="num">Net spent</th>
                <th scope="col" className="num">Remaining</th>
                <th scope="col" className="meter-col"><span className="sr-only">Used</span></th>
              </tr>
            </thead>
            <tbody>
              {b.lines.map((l) => (
                <tr key={l.categoryId}>
                  <th scope="row">
                    <span className="swatch" style={{ background: color(l.color) }} aria-hidden="true" />
                    {l.name}
                  </th>
                  <td className="num">
                    <button type="button" className="link-button" onClick={() => setEditing(l)}>
                      {l.amount == null ? 'Set' : money(l.amount, { dash: false })}
                    </button>
                    {l.source === 'month' && <span className="badge" title="Set for this month only">this month</span>}
                  </td>
                  <td className="num">{money(l.net)}</td>
                  <td className={`num ${(l.remaining ?? 0) < 0 ? 'negative' : ''}`}>{l.remaining == null ? '' : signed(l.remaining, { dash: false })}</td>
                  <td className="meter-col">{l.amount != null && l.amount > 0 && <Meter value={l.net} max={l.amount} color={color(l.color)} label={`${l.name} budget used`} />}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row">Overall</th>
                <td className="num">{b.totalBudget == null ? 'Not set' : money(b.totalBudget, { dash: false })}</td>
                <td />
                <td className={`num ${(b.overallRemaining ?? 0) < 0 ? 'negative' : ''}`}>{b.overallRemaining == null ? '' : signed(b.overallRemaining, { dash: false })}</td>
                <td />
              </tr>
            </tfoot>
          </table>
        </div>
      )}
      {editing && <BudgetSheet line={editing} onClose={() => setEditing(null)} />}
    </section>
  );
}

function BudgetSheet({ line, onClose }: { line: BudgetLine; onClose: () => void }) {
  const { ym } = useWorkspace();
  const set = useSetBudget();
  const [text, setText] = useState(line.amount == null ? '' : String(line.amount));
  const [scope, setScope] = useState<'month' | 'future'>('future');
  const [error, setError] = useState<string | null>(null);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseAmounts(text);
    if (!parsed.ok) return setError(parsed.error);
    const amount = parsed.amounts.length ? parsed.amounts.reduce((a, b) => a + b, 0) : null;
    set.mutate({ ym, categoryId: line.categoryId, amount, scope }, { onSuccess: () => (toast('Budget saved', 'success'), onClose()), onError: (err) => setError(err.message) });
  };

  return (
    <Sheet title={`Budget · ${line.name}`} onClose={onClose}>
      <form className="form-stack" onSubmit={submit}>
        <label className="field field-amount">
          <span>Monthly budget (leave empty for none)</span>
          <input inputMode="decimal" value={text} onChange={(e) => setText(e.target.value)} placeholder="Not set" />
        </label>
        <ScopeChoice value={scope} onChange={setScope} month={ymLabel(ym)} />
        <p className="muted small">Earlier months keep their budgets either way.</p>
        {error && <p className="error-text" role="alert">{error}</p>}
        <button className="btn btn-primary btn-block" disabled={set.isPending}>
          Save budget
        </button>
      </form>
    </Sheet>
  );
}

// ---------- Recurring ----------

function Recurring() {
  const { ym, today, timing } = useWorkspace();
  const recurring = useRecurring(ym);
  const action = useOccurrenceAction();
  const [editingItem, setEditingItem] = useState<RecurringItem | 'new' | null>(null);
  const [editingOccurrence, setEditingOccurrence] = useState<Occurrence | null>(null);
  const r = recurring.data;

  const pay = (o: Occurrence) => {
    const date = o.dueDate <= today ? o.dueDate : today;
    action.mutate({ id: o.id, action: 'pay', date }, { onSuccess: () => toast(`${o.description} marked ${o.kind === 'Income' ? 'received' : 'paid'}`, 'success') });
  };

  return (
    <section className={`card ${recurring.isPlaceholderData ? 'is-refetching' : ''}`}>
      <div className="card-head">
        <h2>
          <Icon name="calendar" className="card-icon" /> Recurring this month
        </h2>
        <button type="button" className="btn btn-sm btn-primary" onClick={() => setEditingItem('new')}>
          <Icon name="plus" size={14} /> Recurring item
        </button>
      </div>
      <p className="muted small">
        Rent, bills, subscriptions… Each month gets a planned occurrence. It only becomes spending when you mark it paid, which creates one linked entry.
      </p>
      {r && r.occurrences.length === 0 && <p className="muted">Nothing recurring in {ymLabel(ym)}.</p>}
      {r && r.occurrences.length > 0 && (
        <ul className="entry-list occurrences">
          {r.occurrences.map((o) => (
            <li key={o.id} className={`occ-${o.status.toLowerCase()}`}>
              <span className="entry-name">
                {o.description}
                <span className="muted small">
                  {' '}
                  · {o.categoryName} · due {dayLabel(o.dueDate).day}
                  {o.isOverridden && ' · edited for this month'}
                </span>
              </span>
              <span className={`badge ${o.status === 'Paid' ? 'badge-good' : o.status === 'Skipped' ? '' : 'badge-plan'}`}>{o.status === 'Paid' && o.kind === 'Income' ? 'Received' : o.status}</span>
              <span className="num">{money(o.amount)}</span>
              {o.status === 'Planned' && (
                <>
                  <button type="button" className="btn btn-sm btn-primary" disabled={timing === 'future' || action.isPending} title={timing === 'future' ? 'Future months can only be planned' : undefined} onClick={() => pay(o)}>
                    {o.kind === 'Income' ? 'Received' : 'Pay'}
                  </button>
                  <button type="button" className="btn-ghost" onClick={() => setEditingOccurrence(o)}>
                    Edit
                  </button>
                  <button type="button" className="btn-ghost" onClick={() => action.mutate({ id: o.id, action: 'skip' })}>
                    Skip
                  </button>
                </>
              )}
              {o.status === 'Paid' && (
                <button type="button" className="btn-ghost" onClick={() => confirm('Undo this payment? The linked entry is removed.') && action.mutate({ id: o.id, action: 'unpay' })}>
                  Undo
                </button>
              )}
              {o.status === 'Skipped' && (
                <button type="button" className="btn-ghost" onClick={() => action.mutate({ id: o.id, action: 'unskip' })}>
                  Restore
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {r && r.plannedUnpaid > 0 && (
        <p className="muted small">
          Still planned: <b>{money(r.plannedUnpaid)}</b>
        </p>
      )}

      {r && r.items.length > 0 && (
        <>
          <h3 className="mini-head">All recurring items</h3>
          <ul className="plain-list">
            {r.items.map((i) => (
              <li key={i.seriesId}>
                <span>
                  {i.description} <span className="muted small">· {money(i.amount)} on day {i.dayOfMonth} · from {ymLabel(i.start)}{i.end ? ` to ${ymLabel(i.end)}` : ''}{!i.isActive ? ' · stopped' : ''}</span>
                </span>
                <button type="button" className="btn-ghost" onClick={() => setEditingItem(i)}>
                  <Icon name="pencil" size={13} /> Edit
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      {editingItem && <RecurringSheet item={editingItem === 'new' ? null : editingItem} onClose={() => setEditingItem(null)} />}
      {editingOccurrence && <OccurrenceSheet o={editingOccurrence} onClose={() => setEditingOccurrence(null)} />}
    </section>
  );
}

function RecurringSheet({ item, onClose }: { item: RecurringItem | null; onClose: () => void }) {
  const { ym, me } = useWorkspace();
  const categories = useCategories();
  const save = useSaveRecurring();
  const stop = useStopRecurring();
  const [kind, setKind] = useState<'Expense' | 'Income'>(item?.kind ?? 'Expense');
  const [description, setDescription] = useState(item?.description ?? '');
  const [amount, setAmount] = useState(item ? String(item.amount) : '');
  const [categoryId, setCategoryId] = useState<number | null>(item?.categoryId ?? null);
  const [day, setDay] = useState(item?.dayOfMonth ?? 1);
  const [method, setMethod] = useState(item?.paymentMethod ?? '');
  const [start, setStart] = useState(item?.start ?? ym);
  const [end, setEnd] = useState(item?.end ?? '');
  const [error, setError] = useState<string | null>(null);
  const options = (categories.data ?? []).filter((c) => c.type === kind && !c.isArchived);

  useEffect(() => {
    if (!options.some((c) => c.id === categoryId)) setCategoryId(options[0]?.id ?? null);
  }, [kind, categories.data]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseAmounts(amount);
    if (!parsed.ok || parsed.amounts.length !== 1) return setError('Enter one amount');
    try {
      await save.mutateAsync({
        id: item?.id,
        from: item ? (ym > item.start ? ym : item.start) : undefined,
        kind, description, amount: parsed.amounts[0], categoryId, dayOfMonth: day, paymentMethod: method, start, end: end || null, version: item?.version,
      });
      toast(item ? `Saved from ${ymLabel(ym > (item?.start ?? ym) ? ym : item!.start)} onward` : 'Recurring item added', 'success');
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save');
    }
  };

  return (
    <Sheet title={item ? 'Edit recurring item' : 'New recurring item'} onClose={onClose}>
      <form className="form-stack" onSubmit={submit}>
        {!item && (
          <div className="segmented" role="radiogroup" aria-label="Kind">
            {(['Expense', 'Income'] as const).map((k) => (
              <button key={k} type="button" role="radio" aria-checked={kind === k} className={kind === k ? 'on' : ''} onClick={() => setKind(k)}>
                {k === 'Expense' ? 'Spending' : 'Income'}
              </button>
            ))}
          </div>
        )}
        <label className="field">
          <span>Name</span>
          <input value={description} maxLength={200} onChange={(e) => setDescription(e.target.value)} placeholder="e.g. Rent, Internet, Netflix" />
        </label>
        <div className="quick-row">
          <label className="field">
            <span>Amount</span>
            <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
          </label>
          <label className="field">
            <span>Day of month</span>
            <input type="number" min={1} max={31} value={day} onChange={(e) => setDay(Number(e.target.value))} />
          </label>
        </div>
        <label className="field">
          <span>Category</span>
          <select value={categoryId ?? ''} onChange={(e) => setCategoryId(Number(e.target.value) || null)}>
            {options.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Paid with</span>
          <select value={method} onChange={(e) => setMethod(e.target.value)}>
            <option value="">—</option>
            {me.paymentMethods.map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        <div className="quick-row">
          <label className="field">
            <span>Starts</span>
            <input type="month" value={start} disabled={!!item} onChange={(e) => e.target.value && setStart(e.target.value)} />
          </label>
          <label className="field">
            <span>Ends (optional)</span>
            <input type="month" value={end ?? ''} min={start} onChange={(e) => setEnd(e.target.value)} />
          </label>
        </div>
        {item && <p className="muted small">Changes apply from {ymLabel(ym > item.start ? ym : item.start)} onward. Earlier months and anything already paid keep their values. To change only this month, edit the occurrence instead.</p>}
        {error && <p className="error-text" role="alert">{error}</p>}
        <div className="form-actions">
          {item && (
            <button type="button" className="btn btn-danger-ghost" onClick={() => confirm(`Stop “${item.description}” from ${ymLabel(ym)} on? Paid months are kept.`) && stop.mutate({ id: item.id, from: ym }, { onSuccess: onClose })}>
              Stop from {ymLabel(ym)}
            </button>
          )}
          <span className="spacer" />
          <button className="btn btn-primary" disabled={save.isPending}>
            Save
          </button>
        </div>
      </form>
    </Sheet>
  );
}

function OccurrenceSheet({ o, onClose }: { o: Occurrence; onClose: () => void }) {
  const update = useUpdateOccurrence();
  const [amount, setAmount] = useState(String(o.amount));
  const [due, setDue] = useState(o.dueDate);
  const [note, setNote] = useState(o.note);
  const [error, setError] = useState<string | null>(null);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseAmounts(amount);
    if (!parsed.ok || parsed.amounts.length !== 1) return setError('Enter one amount');
    update.mutate({ id: o.id, version: o.version, amount: parsed.amounts[0], dueDate: due, note }, { onSuccess: () => (toast('Saved for this month only', 'success'), onClose()), onError: (err) => setError(err.message) });
  };
  return (
    <Sheet title={`${o.description} · this month only`} onClose={onClose}>
      <form className="form-stack" onSubmit={submit}>
        <label className="field field-amount">
          <span>Amount</span>
          <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </label>
        <label className="field">
          <span>Due date</span>
          <input type="date" value={due} min={`${o.dueDate.slice(0, 7)}-01`} onChange={(e) => e.target.value && setDue(e.target.value)} />
        </label>
        <label className="field">
          <span>Note</span>
          <input value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
        </label>
        {error && <p className="error-text" role="alert">{error}</p>}
        <button className="btn btn-primary btn-block" disabled={update.isPending}>
          Save
        </button>
      </form>
    </Sheet>
  );
}

// ---------- Month settings & copy ----------

function MonthSettingsCard() {
  const { ym } = useWorkspace();
  const settings = useMonthSettings(ym);
  const save = useSaveMonthSettings();
  const copy = useCopyMonth();
  const s = settings.data;
  const [opening, setOpening] = useState('');
  const [reserved, setReserved] = useState('');
  const [note, setNote] = useState('');
  const [from, setFrom] = useState(shiftKey(ym, -1));
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!s) return;
    setOpening(s.openingBalance == null ? '' : String(s.openingBalance));
    setReserved(s.reservedSavings ? String(s.reservedSavings) : '');
    setNote(s.note);
  }, [s]);
  useEffect(() => setFrom(shiftKey(ym, -1)), [ym]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const o = opening.trim() === '' ? null : parseSigned(opening);
    const r = reserved.trim() === '' ? { ok: true as const, value: 0 } : parseSigned(reserved);
    if ((o && !o.ok) || !r.ok || r.value < 0) return setError('Enter numbers for the balances');
    setError(null);
    save.mutate(
      { ym, openingBalance: o && o.ok ? o.value : null, reservedSavings: r.value, note, version: s?.version || undefined },
      { onSuccess: () => toast('Month settings saved', 'success'), onError: (err) => setError(err.message) },
    );
  };

  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="sliders" className="card-icon" /> {ymLabel(ym)} settings
        </h2>
      </div>
      <form className="form-grid" onSubmit={submit}>
        <label className="field">
          <span>Opening balance</span>
          <input inputMode="decimal" value={opening} onChange={(e) => setOpening(e.target.value)} placeholder="0" />
          <small className="muted">
            {s?.carryoverEnabled ? 'Carryover is on: later months open with the previous closing balance; this is used for the first month only.' : 'Money you start the month with. It is not income.'}
          </small>
        </label>
        <label className="field">
          <span>Reserved savings</span>
          <input inputMode="decimal" value={reserved} onChange={(e) => setReserved(e.target.value)} placeholder="0" />
          <small className="muted">Set aside before the daily allowance is worked out.</small>
        </label>
        <label className="field field-wide">
          <span>Note</span>
          <input value={note} maxLength={1000} onChange={(e) => setNote(e.target.value)} />
        </label>
        {error && <p className="error-text field-wide" role="alert">{error}</p>}
        <div className="form-actions">
          <button className="btn btn-primary" disabled={save.isPending}>
            Save
          </button>
        </div>
      </form>

      <div className="copy-row">
        <h3 className="mini-head">Start from another month</h3>
        <p className="muted small">Copies column layouts, budgets and recurring plans into {ymLabel(ym)}. Paid entries are never copied.</p>
        <div className="inline-form">
          <label className="field">
            <span>Copy from</span>
            <input type="month" value={from} onChange={(e) => e.target.value && setFrom(e.target.value)} />
          </label>
          <button
            type="button"
            className="btn"
            disabled={copy.isPending || from === ym}
            onClick={() =>
              copy.mutate(
                { ym, from, layout: true, budgets: true, recurring: true },
                { onSuccess: (r) => toast(`Copied ${r.budgetsCopied} budgets, ${r.layoutsCopied} layouts; ${r.occurrencesPlanned} recurring items planned`, 'success') },
              )
            }
          >
            Copy setup
          </button>
        </div>
      </div>
    </section>
  );
}
