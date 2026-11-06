import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  newRequestId,
  useAddSalaryPayment,
  useCategories,
  useDeleteReceipt,
  useIncome,
  useSaveReceipt,
  useSaveStatement,
} from '../api/hooks';
import type { Allocation, IncomeMonth, Receipt, SalaryComponent, SalaryStatement } from '../api/types';
import { Icon } from '../components/Icon';
import { Sheet } from '../components/Sheet';
import { toast } from '../components/Toast';
import { SectionTitle, useWorkspace } from '../components/Workspace';
import { parseSigned } from '../lib/amounts';
import { dayLabel, shiftKey, ymLabel, ymLongLabel } from '../lib/dates';
import { money, signed } from '../lib/format';
import { checkAmount, checkDate, checkMonth, firstError, MAX_DATE, MIN_DATE } from '../lib/validate';

const KINDS = ['Base', 'Allowance', 'Bonus', 'Overtime', 'Arrears', 'Deduction', 'Adjustment'] as const;
// A saved salary may fund a month up to a year after it was earned (same limit as the server).
const MAX_FUNDING_LAG = 12;

const parseAmount = (text: string) => {
  const r = parseSigned(text);
  return r.ok ? r.value : NaN;
};

export function IncomeSection() {
  const { ym } = useWorkspace();
  const income = useIncome(ym);
  const d = income.data;
  return (
    <div className={`page ${income.isPlaceholderData ? 'is-refetching' : ''}`}>
      <SectionTitle>Salary & Income</SectionTitle>
      {income.isError && <p className="error-text">{income.error.message}</p>}
      {!d && !income.isError && <div className="loading">Loading…</div>}
      {d && <IncomeBody d={d} />}
    </div>
  );
}

function IncomeBody({ d }: { d: IncomeMonth }) {
  const { ym, me } = useWorkspace();
  return (
    <>
      <dl className="tiles">
        <div className="tile tile-budget">
          <dt>Expected net salary</dt>
          <dd>{money(d.expectedSalary)}</dd>
          <span className="tile-hint">planning figure</span>
        </div>
        <div className="tile tile-income">
          <dt>Salary received</dt>
          <dd>{money(d.salaryReceived)}</dd>
        </div>
        <div className="tile tile-spent">
          <dt>{d.salaryOverpaid > 0 ? 'Overpaid' : 'Salary still due'}</dt>
          <dd>{money(d.salaryOverpaid > 0 ? d.salaryOverpaid : d.salaryDue)}</dd>
          <span className="tile-hint">not available until received</span>
        </div>
        <div className="tile tile-safe">
          <dt>Other received income</dt>
          <dd>{money(d.otherIncomeReceived)}</dd>
          {d.expectedOtherIncome > 0 && <span className="tile-hint">+{money(d.expectedOtherIncome)} expected</span>}
        </div>
        <div className="tile tile-avg">
          <dt>Total income available</dt>
          <dd>{money(d.totalReceivedIncome)}</dd>
          <span className="tile-hint">received, for {ymLabel(ym)}</span>
        </div>
      </dl>

      <p className="hint">
        Rule: a month's salary funds the budget month {me.salaryLagMonths === 0 ? 'it is earned in' : `${me.salaryLagMonths} month${me.salaryLagMonths > 1 ? 's' : ''} later`} (
        {me.salaryLagMonths === 1 ? 'October salary funds November' : 'change in Settings'}). Each payment counts once, in the budget month it's
        allocated to.
      </p>

      {d.funding.length === 0 ? (
        <NoSalary ym={ym} lag={d.salaryLagMonths} />
      ) : (
        d.funding.map((s) => <StatementCard key={s.earned} s={s} />)
      )}

      {d.earnedThisMonth && d.earnedThisMonth.funded !== ym && (
        <p className="muted small">
          {ymLongLabel(ym)} salary ({money(d.earnedThisMonth.expectedNet)} expected) funds{' '}
          <Link className="link" to={`/m/${d.earnedThisMonth.funded}/income`}>
            {ymLongLabel(d.earnedThisMonth.funded)}
          </Link>
          .
        </p>
      )}

      <OtherIncome d={d} />
    </>
  );
}

function NoSalary({ ym, lag }: { ym: string; lag: number }) {
  const earned = shiftKey(ym, -lag);
  const [open, setOpen] = useState(false);
  return (
    <section className="card setup">
      <div>
        <h2>No salary funds {ymLongLabel(ym)} yet</h2>
        <p className="muted">
          Add a salary plan in <Link to="/settings#salary">Settings</Link> so every month knows what to expect, or enter {ymLongLabel(earned)}'s salary here.
        </p>
      </div>
      <button type="button" className="btn btn-primary" onClick={() => setOpen(true)}>
        Enter {ymLabel(earned)} salary
      </button>
      {open && (
        <StatementEditor
          s={{ id: null, version: 0, earned, funded: ym, isPlanned: true, note: '', components: [{ kind: 'Base', label: 'Base salary', amount: 0 }], expectedNet: 0, received: 0, outstanding: 0, overpaid: 0, status: 'Expected', payments: [] }}
          onClose={() => setOpen(false)}
        />
      )}
    </section>
  );
}

function StatementCard({ s }: { s: SalaryStatement }) {
  const [editing, setEditing] = useState(false);
  const [paying, setPaying] = useState<Receipt | 'new' | null>(null);
  const remove = useDeleteReceipt();
  const statusClass = { Expected: 'badge-plan', Partial: 'badge-warn', Received: 'badge-good', Overpaid: 'badge-warn' }[s.status] ?? '';

  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="wallet" className="card-icon" /> {ymLongLabel(s.earned)} salary
          <span className="muted small"> → funds {ymLongLabel(s.funded)}</span>
        </h2>
        <span className={`badge ${statusClass}`}>{s.status}</span>
      </div>
      {s.isPlanned && <p className="muted small">From your salary plan. It isn't saved yet: editing or recording a payment saves it, and later plan changes won't rewrite it.</p>}

      <table className="kv-table">
        <tbody>
          {s.components.map((c, i) => (
            <tr key={i}>
              <th scope="row">
                {c.label || c.kind} <span className="muted small">{c.kind !== (c.label || c.kind) ? `· ${c.kind}` : ''}</span>
              </th>
              <td className="num">{c.kind === 'Deduction' ? `−${money(c.amount)}` : c.kind === 'Adjustment' ? signed(c.amount) : money(c.amount)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row">Expected net salary</th>
            <td className="num">{money(s.expectedNet, { dash: false })}</td>
          </tr>
          <tr>
            <th scope="row">Received so far</th>
            <td className="num">{money(s.received, { dash: false })}</td>
          </tr>
          <tr>
            <th scope="row">{s.overpaid > 0 ? 'Overpaid' : 'Still due'}</th>
            <td className={`num ${s.overpaid > 0 ? 'negative' : ''}`}>{money(s.overpaid > 0 ? s.overpaid : s.outstanding, { dash: false })}</td>
          </tr>
        </tfoot>
      </table>

      <div className="btn-row">
        <button type="button" className="btn" onClick={() => setEditing(true)}>
          <Icon name="pencil" size={14} /> Edit salary
        </button>
        <button type="button" className="btn btn-primary" onClick={() => setPaying('new')}>
          <Icon name="plus" size={14} /> Record payment
        </button>
      </div>

      <h3 className="mini-head">Payments</h3>
      {s.payments.length === 0 ? (
        <p className="muted small">Nothing received yet. Pending salary doesn't count as available money.</p>
      ) : (
        <ul className="entry-list">
          {s.payments.map((p) => (
            <li key={p.id}>
              <span className="entry-name">
                {p.receivedDate ? dayLabel(p.receivedDate).day : ''} · {p.paymentMethod || 'Payment'}
                <span className="muted small"> → {p.allocations.map((a) => `${ymLabel(a.month)} ${money(a.amount)}`).join(', ')}</span>
                {p.unallocated > 0 && <span className="badge badge-warn">{money(p.unallocated)} unallocated</span>}
              </span>
              <span className="num">{money(p.amount)}</span>
              <button type="button" className="btn-ghost btn-round" aria-label="Edit payment" onClick={() => setPaying(p)}>
                <Icon name="pencil" size={14} />
              </button>
              <button
                type="button"
                className="btn-ghost btn-round"
                aria-label="Delete payment"
                onClick={() => confirm(`Delete this ${money(p.amount)} payment?`) && remove.mutate(p.id)}
              >
                <Icon name="x" size={14} />
              </button>
            </li>
          ))}
        </ul>
      )}

      {editing && <StatementEditor s={s} onClose={() => setEditing(false)} />}
      {paying && <ReceiptSheet kind="Salary" statement={s} receipt={paying === 'new' ? null : paying} onClose={() => setPaying(null)} />}
    </section>
  );
}

function StatementEditor({ s, onClose }: { s: SalaryStatement; onClose: () => void }) {
  const save = useSaveStatement();
  const [funded, setFunded] = useState(s.funded);
  const [note, setNote] = useState(s.note);
  const [rows, setRows] = useState(s.components.map((c) => ({ ...c, text: String(c.amount) })));
  const [error, setError] = useState<string | null>(null);

  const preview = rows.reduce((sum, r) => {
    const v = parseAmount(r.text);
    if (Number.isNaN(v)) return sum;
    return sum + (r.kind === 'Deduction' ? -Math.abs(v) : r.kind === 'Adjustment' ? v : Math.abs(v));
  }, 0);

  // Same rules as the server, shown before saving.
  const lineErrors = rows.map((r) => {
    const parsed = parseSigned(r.text);
    if (!parsed.ok) return `${r.label || r.kind}: ${parsed.error}`;
    return r.kind !== 'Adjustment' && parsed.value < 0 ? `${r.kind} can't be negative; use an Adjustment for signed changes` : null;
  });
  const latestFunded = shiftKey(s.earned, MAX_FUNDING_LAG);
  const problem = firstError(
    checkMonth(funded, 'Funded month'),
    funded < s.earned && "A salary can't fund a month before it was earned",
    funded > latestFunded && `A salary can fund a month at most ${MAX_FUNDING_LAG} months after it was earned`,
    ...lineErrors,
    preview < 0 && "Deductions can't be more than the salary; the expected net would be negative",
  );

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (problem) return setError(problem);
    const components: SalaryComponent[] = rows.map((r) => ({ kind: r.kind, label: r.label, amount: parseAmount(r.text) }));
    save.mutate(
      { earned: s.earned, funded, note, components, version: s.id ? s.version : undefined },
      { onSuccess: () => (toast('Salary saved', 'success'), onClose()), onError: (err) => setError(err.message) },
    );
  };

  return (
    <Sheet title={`${ymLongLabel(s.earned)} salary`} onClose={onClose} wide>
      <form className="form-stack" onSubmit={submit}>
        <label className="field">
          <span>Funds budget month</span>
          <input type="month" value={funded} min={s.earned} max={latestFunded} onChange={(e) => e.target.value && setFunded(e.target.value)} />
        </label>
        <div className="table-scroll">
          <table className="data-table edit-table">
            <thead>
              <tr>
                <th scope="col">Type</th>
                <th scope="col">Label</th>
                <th scope="col" className="num">Amount</th>
                <th scope="col"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i}>
                  <td>
                    <select value={r.kind} aria-label="Type" onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, kind: e.target.value } : x)))}>
                      {KINDS.map((k) => (
                        <option key={k}>{k}</option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input value={r.label} aria-label="Label" maxLength={100} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)))} />
                  </td>
                  <td>
                    <input
                      className="num"
                      inputMode="decimal"
                      value={r.text}
                      aria-label="Amount"
                      aria-invalid={lineErrors[i] ? true : undefined}
                      title={lineErrors[i] ?? undefined}
                      onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)))}
                    />
                  </td>
                  <td className="actions">
                    <button type="button" className="btn-ghost btn-round" aria-label="Duplicate row" onClick={() => setRows([...rows.slice(0, i + 1), { ...r }, ...rows.slice(i + 1)])}>
                      <Icon name="plus" size={14} />
                    </button>
                    <button type="button" className="btn-ghost btn-round" aria-label="Remove row" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                      <Icon name="x" size={14} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <button type="button" className="btn btn-sm" onClick={() => setRows([...rows, { kind: 'Bonus', label: '', amount: 0, text: '' }])}>
          <Icon name="plus" size={14} /> Add line
        </button>
        <p className="muted small">
          Deductions already taken out of your pay reduce the expected net; they are never counted as expenses. Adjustment can be + or −.
        </p>
        <label className="field">
          <span>Note</span>
          <input value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
        </label>
        <p className="preview">
          Expected net: <b>{money(preview, { dash: false })}</b>
        </p>
        {(problem ?? error) && <p className="error-text" role="alert">{problem ?? error}</p>}
        <div className="form-actions">
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={save.isPending || !!problem}>
            {save.isPending ? 'Saving…' : 'Save salary'}
          </button>
        </div>
      </form>
    </Sheet>
  );
}

// ---------- Receipts (salary payments and other income) ----------

function ReceiptSheet({
  kind,
  statement,
  receipt,
  defaultMonth,
  onClose,
}: {
  kind: 'Salary' | 'Other';
  statement?: SalaryStatement;
  receipt: Receipt | null;
  defaultMonth?: string;
  onClose: () => void;
}) {
  const { today, me, ym } = useWorkspace();
  const categories = useCategories();
  const addPayment = useAddSalaryPayment();
  const saveReceipt = useSaveReceipt();
  const incomeCategories = (categories.data ?? []).filter((c) => c.type === 'Income' && !c.isArchived);
  const target = statement?.funded ?? defaultMonth ?? ym;

  const [status, setStatus] = useState<'Received' | 'Expected'>(receipt?.status ?? 'Received');
  const [date, setDate] = useState(receipt?.receivedDate ?? receipt?.expectedDate ?? (target === ym ? today : `${target}-01` > today ? today : `${target}-01`));
  const [amount, setAmount] = useState(receipt ? String(receipt.amount) : statement && statement.outstanding > 0 ? String(statement.outstanding) : '');
  const [categoryId, setCategoryId] = useState<number | null>(receipt?.categoryId ?? incomeCategories[0]?.id ?? null);
  const [method, setMethod] = useState(receipt?.paymentMethod ?? 'Bank transfer');
  const [description, setDescription] = useState(receipt?.description ?? '');
  const [split, setSplit] = useState((receipt?.allocations.length ?? 0) > 1);
  const [allocations, setAllocations] = useState<{ month: string; text: string }[]>(
    receipt?.allocations.map((a) => ({ month: a.month, text: String(a.amount) })) ?? [{ month: target, text: '' }],
  );
  const [error, setError] = useState<string | null>(null);
  const [requestId] = useState(newRequestId);

  // Checked as you type, with the server's rules.
  const received = status === 'Received' || kind === 'Salary';
  const amountCheck = checkAmount(amount);
  const amountError = amount.trim() ? firstError(amountCheck) : null;
  const dateError = firstError(
    checkDate(date, {
      label: received ? 'Received date' : 'Expected date',
      max: received ? today : undefined,
      maxMessage: kind === 'Salary' ? "A payment can't be dated after today" : "Received money can't be dated after today; mark it expected instead",
    }),
  );
  const splitChecks = split ? allocations.map((a) => ({ month: a.month, monthCheck: checkMonth(a.month, 'Each month'), amount: checkAmount(a.text) })) : [];
  const splitTotal = splitChecks.reduce((sum, c) => sum + (c.amount.ok ? c.amount.value ?? 0 : 0), 0);
  const splitError = split
    ? firstError(
        allocations.length === 0 && 'Add a month to split into',
        ...splitChecks.flatMap((c) => [c.monthCheck, c.amount]),
        new Set(allocations.map((a) => a.month)).size !== allocations.length && 'Allocate to each month once',
        amountCheck.ok && splitTotal > (amountCheck.value ?? 0) && `The split (${money(splitTotal)}) is more than the amount (${money(amountCheck.value)})`,
      )
    : null;
  const problem = firstError(amountCheck, dateError, splitError);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (problem || !amountCheck.ok || amountCheck.value == null) return setError(problem ?? 'Enter the amount');
    const value = amountCheck.value;
    let allocs: Allocation[] | null = null;
    if (split) {
      allocs = splitChecks.map((c) => ({ month: c.month, amount: c.amount.ok ? c.amount.value ?? 0 : 0 }));
    } else if (receipt) {
      allocs = [{ month: allocations[0]?.month ?? target, amount: value }];
    }

    const input = {
      kind,
      status: kind === 'Salary' ? ('Received' as const) : status,
      receivedDate: status === 'Received' || kind === 'Salary' ? date : null,
      expectedDate: status === 'Expected' ? date : null,
      amount: value,
      categoryId: kind === 'Other' ? categoryId : null,
      description,
      paymentMethod: method,
      allocations: allocs,
      defaultMonth: target,
    };
    const done = { onSuccess: () => (toast('Saved', 'success'), onClose()), onError: (err: Error) => setError(err.message) };
    if (receipt) saveReceipt.mutate({ ...input, id: receipt.id, version: receipt.version }, done);
    else if (kind === 'Salary' && statement) addPayment.mutate({ ...input, earned: statement.earned, clientRequestId: requestId }, done);
    else saveReceipt.mutate({ ...input, clientRequestId: requestId }, done);
  };

  const pending = addPayment.isPending || saveReceipt.isPending;
  return (
    <Sheet title={kind === 'Salary' ? (receipt ? 'Edit salary payment' : 'Record salary payment') : receipt ? 'Edit income' : 'Add other income'} onClose={onClose}>
      <form className="form-stack" onSubmit={submit}>
        {kind === 'Other' && (
          <div className="segmented" role="radiogroup" aria-label="Status">
            {(['Received', 'Expected'] as const).map((s) => (
              <button key={s} type="button" role="radio" aria-checked={status === s} className={status === s ? 'on' : ''} onClick={() => setStatus(s)}>
                {s}
              </button>
            ))}
          </div>
        )}
        <label className="field field-amount">
          <span>Amount ({me.currency})</span>
          <input inputMode="decimal" value={amount} aria-invalid={amountError ? true : undefined} onChange={(e) => setAmount(e.target.value)} placeholder="0" />
          {amountError && <small className="error-text">{amountError}</small>}
        </label>
        <div className="quick-row">
          <label className="field">
            <span>{status === 'Expected' && kind === 'Other' ? 'Expected on' : 'Received on'}</span>
            <input
              type="date"
              value={date}
              min={MIN_DATE}
              max={received ? today : MAX_DATE}
              aria-invalid={dateError ? true : undefined}
              onChange={(e) => e.target.value && setDate(e.target.value)}
            />
            {dateError && <small className="error-text">{dateError}</small>}
          </label>
          <label className="field">
            <span>Received via</span>
            <select value={method} onChange={(e) => setMethod(e.target.value)}>
              {me.paymentMethods.map((m) => (
                <option key={m}>{m}</option>
              ))}
            </select>
          </label>
        </div>
        {kind === 'Other' && (
          <label className="field">
            <span>Category</span>
            <select value={categoryId ?? ''} onChange={(e) => setCategoryId(Number(e.target.value) || null)}>
              {incomeCategories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
        )}
        <label className="field">
          <span>Note</span>
          <input value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} placeholder={kind === 'Salary' ? 'e.g. first instalment' : 'e.g. client payment'} />
        </label>

        <label className="toggle">
          <input type="checkbox" checked={split} onChange={(e) => setSplit(e.target.checked)} />
          <span>Split across budget months (otherwise it all funds {ymLabel(allocations[0]?.month ?? target)})</span>
        </label>
        {split && (
          <div className="alloc-list">
            {allocations.map((a, i) => (
              <div className="alloc-row" key={i}>
                <input type="month" value={a.month} aria-label="Budget month" onChange={(e) => setAllocations(allocations.map((x, j) => (j === i ? { ...x, month: e.target.value } : x)))} />
                <input inputMode="decimal" className="num" placeholder="Amount" value={a.text} aria-label="Amount" onChange={(e) => setAllocations(allocations.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)))} />
                <button type="button" className="btn-ghost btn-round" aria-label="Remove split" onClick={() => setAllocations(allocations.filter((_, j) => j !== i))}>
                  <Icon name="x" size={14} />
                </button>
              </div>
            ))}
            <button type="button" className="btn btn-sm" onClick={() => setAllocations([...allocations, { month: shiftKey(allocations.at(-1)?.month ?? target, 1), text: '' }])}>
              <Icon name="plus" size={14} /> Another month
            </button>
            {splitError && allocations.some((a) => a.text.trim()) && <small className="error-text">{splitError}</small>}
          </div>
        )}
        {error && <p className="error-text" role="alert">{error}</p>}
        <button className="btn btn-primary btn-block btn-lg" disabled={pending || !!problem} title={problem ?? undefined}>
          {pending ? 'Saving…' : 'Save'}
        </button>
      </form>
    </Sheet>
  );
}

function OtherIncome({ d }: { d: IncomeMonth }) {
  const [editing, setEditing] = useState<Receipt | 'new' | null>(null);
  const remove = useDeleteReceipt();
  const save = useSaveReceipt();
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="income" className="card-icon" /> Other income
        </h2>
        <button type="button" className="btn btn-sm btn-primary" onClick={() => setEditing('new')}>
          <Icon name="plus" size={14} /> Add
        </button>
      </div>
      {d.otherIncome.length === 0 ? (
        <p className="muted small">Freelance work, gifts, refunds from friends… anything that isn't salary.</p>
      ) : (
        <ul className="entry-list">
          {d.otherIncome.map((r) => (
            <li key={r.id} className={r.status === 'Expected' ? 'row-plan' : ''}>
              <span className="entry-name">
                {r.description || r.categoryName}
                <span className="muted small">
                  {' '}
                  · {r.categoryName} · {r.status === 'Expected' ? `expected ${r.expectedDate ? dayLabel(r.expectedDate).day : ''}` : dayLabel(r.receivedDate!).day}
                </span>
                {r.status === 'Expected' && <span className="badge badge-plan">expected</span>}
              </span>
              <span className="num">{money(r.allocations.find((a) => a.month === d.month)?.amount ?? r.amount)}</span>
              <button type="button" className="btn-ghost btn-round" aria-label="Edit" onClick={() => setEditing(r)}>
                <Icon name="pencil" size={14} />
              </button>
              <button
                type="button"
                className="btn-ghost btn-round"
                aria-label="Duplicate"
                onClick={() =>
                  save.mutate({
                    clientRequestId: newRequestId(), kind: 'Other', categoryId: r.categoryId, status: r.status, receivedDate: r.receivedDate,
                    expectedDate: r.expectedDate, amount: r.amount, description: r.description, paymentMethod: r.paymentMethod, defaultMonth: d.month,
                  })
                }
              >
                <Icon name="plus" size={14} />
              </button>
              <button type="button" className="btn-ghost btn-round" aria-label="Delete" onClick={() => confirm('Delete this income?') && remove.mutate(r.id)}>
                <Icon name="x" size={14} />
              </button>
            </li>
          ))}
        </ul>
      )}
      {editing && <ReceiptSheet kind="Other" receipt={editing === 'new' ? null : editing} defaultMonth={d.month} onClose={() => setEditing(null)} />}
    </section>
  );
}
