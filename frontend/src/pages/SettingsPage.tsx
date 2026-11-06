import { useEffect, useState, type FormEvent } from 'react';
import { useLocation } from 'react-router-dom';
import {
  useAccounts,
  useCategories,
  useDeletePlan,
  useImportSheet,
  useMe,
  useSaveAccount,
  useSalaryPlans,
  useSavePlan,
  useUpdateMe,
} from '../api/hooks';
import type { Category, CategoryType, Me, SheetImportResult } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { useColumnEditor, useMoveCategory } from '../components/ColumnEditor';
import { Icon } from '../components/Icon';
import { toast } from '../components/Toast';
import { useCalendar } from '../components/Workspace';
import { parseAmounts } from '../lib/amounts';
import { useSeriesColor } from '../lib/colors';
import { shiftKey, ymLabel, ymLongLabel } from '../lib/dates';
import { money } from '../lib/format';

export function SettingsPage() {
  const me = useMe();
  const { hash } = useLocation();
  useEffect(() => {
    if (hash) document.getElementById(hash.slice(1))?.scrollIntoView({ block: 'start' });
  }, [hash, me.data]);

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <p className="eyebrow">Settings</p>
          <h1>Your tracker</h1>
        </div>
      </header>
      {me.data && <ProfileCard me={me.data} />}
      {me.data && <SalaryPlans me={me.data} />}
      <CategoriesCard />
      <AccountsCard />
      <ImportCard />
      {me.data && <AccountCard me={me.data} />}
    </div>
  );
}

function ProfileCard({ me }: { me: Me }) {
  const update = useUpdateMe();
  const [displayName, setDisplayName] = useState(me.displayName);
  const [currency, setCurrency] = useState(me.currency);
  const [timeZone, setTimeZone] = useState(me.timeZone);
  const [lag, setLag] = useState(me.salaryLagMonths);
  const [carryover, setCarryover] = useState(me.carryoverEnabled);
  const dirty = displayName !== me.displayName || currency !== me.currency || timeZone !== me.timeZone || lag !== me.salaryLagMonths || carryover !== me.carryoverEnabled;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    update.mutate({ displayName, currency, timeZone, salaryLagMonths: lag, carryoverEnabled: carryover }, { onSuccess: () => toast('Settings saved', 'success') });
  };

  const example = (n: number) => `October salary funds ${['October', 'November', 'December', 'January'][n]}`;

  return (
    <section className="card">
      <div className="card-head">
        <h2>Profile & rules</h2>
      </div>
      <form className="form-grid" onSubmit={submit}>
        <label className="field">
          <span>Name</span>
          <input value={displayName} maxLength={100} onChange={(e) => setDisplayName(e.target.value)} />
        </label>
        <label className="field">
          <span>Currency</span>
          <select value={currency} onChange={(e) => setCurrency(e.target.value)}>
            {me.supportedCurrencies.map((c) => (
              <option key={c}>{c}</option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Time zone (for “today” and month boundaries)</span>
          <input value={timeZone} onChange={(e) => setTimeZone(e.target.value)} list="zones" />
          <datalist id="zones">
            <option value="Asia/Karachi" />
            <option value="Asia/Dubai" />
            <option value="Europe/London" />
            <option value="America/New_York" />
          </datalist>
        </label>
        <label className="field">
          <span>Salary funds the budget month…</span>
          <select value={lag} onChange={(e) => setLag(Number(e.target.value))}>
            {[0, 1, 2, 3].map((n) => (
              <option key={n} value={n}>
                {n === 0 ? 'it is earned in' : `${n} month${n > 1 ? 's' : ''} later`} ({example(n)})
              </option>
            ))}
          </select>
        </label>
        <label className="toggle field-wide">
          <input type="checkbox" checked={carryover} onChange={(e) => setCarryover(e.target.checked)} />
          <span>Carry each month's closing balance (positive or negative) into the next month's opening balance. Opening balances are never income.</span>
        </label>
        <p className="muted small field-wide">Changing the salary rule affects months whose salary isn't saved yet. Saved salaries keep the month they fund.</p>
        <div className="form-actions">
          <button className="btn btn-primary" disabled={!dirty || update.isPending}>
            Save
          </button>
        </div>
      </form>
    </section>
  );
}

function SalaryPlans({ me }: { me: Me }) {
  const plans = useSalaryPlans();
  const save = useSavePlan();
  const remove = useDeletePlan();
  const { currentMonth } = useCalendar();
  const [month, setMonth] = useState(shiftKey(currentMonth, -me.salaryLagMonths));
  const [base, setBase] = useState('');
  const [allowances, setAllowances] = useState('');
  const [note, setNote] = useState('');

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const b = parseAmounts(base);
    const a = allowances.trim() ? parseAmounts(allowances) : { ok: true as const, amounts: [0] };
    if (!b.ok || b.amounts.length !== 1 || !a.ok) return toast('Enter the salary as a number, e.g. 55000', 'error');
    save.mutate(
      { effectiveFrom: month, baseSalary: b.amounts[0], allowances: a.amounts[0] ?? 0, note },
      { onSuccess: () => (setBase(''), setAllowances(''), setNote(''), toast(`Salary plan from ${ymLongLabel(month)} saved`, 'success')) },
    );
  };

  return (
    <section className="card" id="salary">
      <div className="card-head">
        <h2>Salary plan</h2>
      </div>
      <p className="muted">
        What you expect to earn from a month onwards. It's a plan: money only counts once you record a payment in that month's Salary & Income tab. A change
        applies from its month on and never rewrites salaries already saved.
      </p>
      <div className="table-scroll">
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">Earned from</th>
              <th scope="col" className="num">Base</th>
              <th scope="col" className="num">Allowances</th>
              <th scope="col">Funds</th>
              <th scope="col">Note</th>
              <th scope="col"><span className="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {(plans.data ?? []).map((p) => (
              <tr key={p.id}>
                <th scope="row">{ymLabel(p.effectiveFrom)}</th>
                <td className="num">{money(p.baseSalary, { dash: false })}</td>
                <td className="num">{money(p.allowances)}</td>
                <td className="calc">{ymLabel(shiftKey(p.effectiveFrom, me.salaryLagMonths))} on</td>
                <td className="muted">{p.note}</td>
                <td className="actions">
                  <button type="button" className="btn-ghost" onClick={() => confirm(`Delete the plan from ${ymLabel(p.effectiveFrom)}?`) && remove.mutate(p.id)}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
            {plans.data?.length === 0 && (
              <tr>
                <td colSpan={6} className="muted">
                  No salary plan yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      <form className="inline-form" onSubmit={submit}>
        <label className="field">
          <span>Earned from</span>
          <input type="month" value={month} onChange={(e) => e.target.value && setMonth(e.target.value)} />
        </label>
        <label className="field">
          <span>Base salary</span>
          <input inputMode="decimal" placeholder="55000" value={base} onChange={(e) => setBase(e.target.value)} />
        </label>
        <label className="field">
          <span>Allowances</span>
          <input inputMode="decimal" placeholder="0" value={allowances} onChange={(e) => setAllowances(e.target.value)} />
        </label>
        <label className="field">
          <span>Note</span>
          <input placeholder="e.g. raise" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
        </label>
        <button className="btn btn-primary" disabled={save.isPending}>
          Save plan
        </button>
      </form>
    </section>
  );
}

function CategoriesCard() {
  const categories = useCategories();
  const list = categories.data ?? [];
  return (
    <section className="card" id="categories">
      <div className="card-head">
        <h2>Categories</h2>
      </div>
      <p className="muted">
        Spending categories are the Daily log columns. Archive one to hide it from new entries; its history and totals stay. Renaming never changes past
        totals.
      </p>
      <CategoryGroup title="Spending" type="Expense" all={list} />
      <CategoryGroup title="Income" type="Income" all={list} />
    </section>
  );
}

function CategoryGroup({ title, type, all }: { title: string; type: CategoryType; all: Category[] }) {
  const editor = useColumnEditor();
  const move = useMoveCategory();
  const color = useSeriesColor();
  const items = all.filter((c) => c.type === type).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  return (
    <div className="column-group">
      <h3>{title}</h3>
      <ul className="column-list">
        {items.map((c, i) => (
          <li key={c.id} className={c.isArchived ? 'archived' : ''}>
            <div className="column-order">
              <button type="button" className="btn-ghost" aria-label={`Move ${c.name} up`} disabled={i === 0} onClick={() => move(all, c.id, -1)}>
                ↑
              </button>
              <button type="button" className="btn-ghost" aria-label={`Move ${c.name} down`} disabled={i === items.length - 1} onClick={() => move(all, c.id, 1)}>
                ↓
              </button>
            </div>
            <span className="swatch swatch-lg" style={{ background: color(c.color) }} aria-hidden="true" />
            <span className="column-name-label">
              {c.name}
              {c.groupName && <span className="muted small"> · {c.groupName}</span>}
              {c.isArchived && <span className="badge">archived</span>}
            </span>
            <span className="muted small column-count">
              {c.transactionCount} {c.transactionCount === 1 ? 'entry' : 'entries'}
            </span>
            <button type="button" className="btn-ghost" onClick={() => editor.open({ id: c.id })}>
              <Icon name="pencil" size={13} /> Edit
            </button>
          </li>
        ))}
      </ul>
      <button type="button" className="btn btn-sm" onClick={() => editor.open({ create: type })}>
        <Icon name="plus" size={14} /> Add {type === 'Expense' ? 'spending' : 'income'} category
      </button>
    </div>
  );
}

function AccountsCard() {
  const accounts = useAccounts();
  const save = useSaveAccount();
  const me = useMe();
  const [name, setName] = useState('');
  const [type, setType] = useState('Wallet');
  return (
    <section className="card" id="accounts">
      <div className="card-head">
        <h2>Accounts</h2>
      </div>
      <p className="muted">Used for transfers between your own accounts (e.g. bank → JazzCash). Transfers never count as income or spending.</p>
      <ul className="plain-list">
        {(accounts.data ?? []).map((a) => (
          <li key={a.id}>
            <span>
              {a.name} <span className="muted small">· {a.type}</span>
            </span>
            <span className="muted small">{a.currency}</span>
          </li>
        ))}
      </ul>
      <form
        className="inline-form"
        onSubmit={(e) => {
          e.preventDefault();
          if (!name.trim()) return;
          save.mutate({ name: name.trim(), type, currency: me.data?.currency ?? 'PKR', openingBalance: 0 }, { onSuccess: () => setName('') });
        }}
      >
        <label className="field">
          <span>New account</span>
          <input value={name} maxLength={100} onChange={(e) => setName(e.target.value)} placeholder="e.g. Meezan Bank" />
        </label>
        <label className="field">
          <span>Type</span>
          <select value={type} onChange={(e) => setType(e.target.value)}>
            {['Cash', 'Wallet', 'Checking', 'Savings', 'CreditCard'].map((t) => (
              <option key={t}>{t}</option>
            ))}
          </select>
        </label>
        <button className="btn" disabled={!name.trim() || save.isPending}>
          Add
        </button>
      </form>
    </section>
  );
}

function ImportCard() {
  const importSheet = useImportSheet();
  const [result, setResult] = useState<SheetImportResult | null>(null);
  const [file, setFile] = useState<File | null>(null);
  return (
    <section className="card" id="import">
      <div className="card-head">
        <h2>Import from Google Sheets</h2>
      </div>
      <ol className="steps">
        <li>
          Open your sheet's <b>Daily Log</b> tab → <b>File → Download → Comma-separated values (.csv)</b>.
        </li>
        <li>Upload it here. Each filled cell replaces that day's entries for its column, so importing twice is safe.</li>
      </ol>
      <form
        className="inline-form"
        onSubmit={(e) => {
          e.preventDefault();
          if (file) importSheet.mutate(file, { onSuccess: setResult });
        }}
      >
        <input type="file" accept=".csv,text/csv" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        <button className="btn btn-primary" disabled={!file || importSheet.isPending}>
          {importSheet.isPending ? 'Importing…' : 'Import'}
        </button>
      </form>
      {result && (
        <div className="import-result">
          <p>
            Read {result.rowsRead} days · <b>{result.entriesCreated}</b> amounts across <b>{result.daysImported}</b> days · {result.notesImported} remarks ·{' '}
            {result.salariesImported} salary rows
          </p>
          {result.categoriesCreated.length > 0 && <p className="muted">New categories: {result.categoriesCreated.join(', ')}</p>}
          {result.warnings.length > 0 && (
            <details>
              <summary>{result.warnings.length} notes</summary>
              <ul>
                {result.warnings.map((w) => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            </details>
          )}
        </div>
      )}
    </section>
  );
}

function AccountCard({ me }: { me: Me }) {
  const { signOut } = useAuth();
  return (
    <section className="card">
      <div className="card-head">
        <h2>Account</h2>
      </div>
      <p>
        Signed in as <b>{me.email}</b>. Sign in with the same account on your phone and laptop: every change shows up on both instantly.
      </p>
      <button type="button" className="btn" onClick={signOut}>
        Sign out
      </button>
    </section>
  );
}
