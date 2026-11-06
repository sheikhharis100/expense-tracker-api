import { useState, type FormEvent } from 'react';
import {
  newRequestId,
  useAccounts,
  useCategories,
  useColumns,
  useDeleteExpense,
  useDuplicateExpense,
  useMe,
  useRefund,
  useUpdateExpense,
} from '../api/hooks';
import { ApiError } from '../api/client';
import { queuedMessage, useCreateOrQueue } from '../api/offline';
import type { Category, Column, ExpenseRow, TxKind, TxStatus } from '../api/types';
import { entryDiffs, isExpenseRow, type EntryDraft } from '../lib/conflict';
import { useToday } from '../lib/dates';
import { money } from '../lib/format';
import { checkAmount, checkCustomValue, checkDate, firstError, LIMITS, MAX_DATE, MIN_DATE } from '../lib/validate';
import { Icon } from './Icon';
import { Sheet } from './Sheet';
import { toast } from './Toast';

// Full editor for one ledger entry, including custom fields. Used by the ledger, the matrix and quick lists.
export function EntrySheet({
  row,
  preset,
  onClose,
}: {
  row: ExpenseRow | null;
  preset?: { date?: string; categoryId?: number; isAdditional?: boolean };
  onClose: () => void;
}) {
  const me = useMe();
  const today = useToday(me.data?.timeZone ?? 'Asia/Karachi');
  const categories = useCategories();
  const accounts = useAccounts();
  const columns = useColumns();
  const create = useCreateOrQueue();
  const update = useUpdateExpense();
  const remove = useDeleteExpense();
  const duplicate = useDuplicateExpense();

  const spending = (categories.data ?? []).filter((c) => c.type === 'Expense' && (!c.isArchived || c.id === row?.categoryId));
  const custom = (columns.data ?? []).filter((c) => !c.builtIn && !c.isArchived && c.type !== 'formula');

  const [kind, setKind] = useState<TxKind>(row?.kind ?? 'Expense');
  const [date, setDate] = useState(row?.date ?? preset?.date ?? today);
  const [amount, setAmount] = useState(row ? String(row.amount) : '');
  const [status, setStatus] = useState<TxStatus>(row?.status ?? 'Paid');
  const [categoryId, setCategoryId] = useState<number | null>(row?.categoryId ?? preset?.categoryId ?? spending[0]?.id ?? null);
  const [description, setDescription] = useState(row?.description ?? '');
  const [method, setMethod] = useState(row?.paymentMethod ?? 'Cash');
  const [additional, setAdditional] = useState(row?.isAdditional ?? preset?.isAdditional ?? false);
  const [accountId, setAccountId] = useState<number | null>(row?.accountId ?? null);
  const [toAccountId, setToAccountId] = useState<number | null>(row?.transferAccountId ?? null);
  const [values, setValues] = useState<Record<string, unknown>>(row?.custom ?? {});
  const [error, setError] = useState<string | null>(null);
  const [requestId] = useState(newRequestId);
  const [refunding, setRefunding] = useState(false);
  // The server's copy when another device saved this entry first; the form keeps what was typed until the user picks.
  const [conflict, setConflict] = useState<ExpenseRow | null>(null);

  const effectiveStatus: TxStatus = kind === 'Refund' ? 'Paid' : date > today ? 'Planned' : status;

  // Checked as you type, with the server's rules; Save stays off while anything is wrong.
  const amountCheck = checkAmount(amount);
  const amountError = amount.trim() ? firstError(amountCheck) : null;
  const dateError = firstError(
    checkDate(date, kind === 'Refund' ? { max: today, maxMessage: "A refund can't be dated after today" } : {}),
  );
  const categoryError = kind === 'Expense' && !categoryId ? 'Choose a category' : null;
  const sameAccount = kind === 'Transfer' && toAccountId != null && toAccountId === accountId;
  const customChecks = custom.map((c) => ({ column: c, check: checkCustomValue(c, values[c.key]) }));
  const problem = firstError(
    amountCheck,
    dateError,
    categoryError,
    kind === 'Transfer' && !toAccountId && 'Choose the account the money moved to',
    sameAccount && 'Choose two different accounts',
    ...customChecks.map((c) => c.check),
  );

  const submit = (e: FormEvent) => {
    e.preventDefault();
    void save(row?.version);
  };

  const save = async (version?: number) => {
    if (problem || !amountCheck.ok || amountCheck.value == null) return setError(problem ?? 'Enter an amount');
    const cleaned = { ...values };
    for (const { column, check } of customChecks) if (check.ok) cleaned[column.key] = check.value;
    const input = {
      date,
      amount: amountCheck.value,
      kind,
      status: effectiveStatus,
      categoryId: kind === 'Transfer' ? null : categoryId,
      description,
      paymentMethod: method,
      isAdditional: kind === 'Expense' ? additional : false,
      accountId,
      transferAccountId: kind === 'Transfer' ? toAccountId : null,
      refundOfId: row?.refundOfId ?? null,
      custom: cleaned,
    };
    try {
      if (row) await update.mutateAsync({ ...input, id: row.id, version });
      else if ((await create.save({ ...input, clientRequestId: requestId })) === 'queued') {
        toast(queuedMessage(), 'info');
        return onClose();
      }
      toast('Saved', 'success');
      onClose();
    } catch (err) {
      // Entered values stay put; a conflict shows what the other device saved and lets the user choose.
      if (row && err instanceof ApiError && err.status === 409 && isExpenseRow(err.detail)) {
        setError(null);
        setConflict(err.detail);
        return;
      }
      setError(err instanceof ApiError && err.status === 409 ? `${err.message} Close and reopen to see it.` : err instanceof Error ? err.message : 'Could not save');
    }
  };

  if (refunding && row) return <RefundSheet row={row} onClose={onClose} />;

  return (
    <Sheet title={row ? (row.kind === 'Refund' ? 'Edit refund' : 'Edit entry') : 'New entry'} onClose={onClose} wide>
      <form className="form-stack" onSubmit={submit}>
        {!row && (
          <div className="segmented" role="radiogroup" aria-label="Type">
            {(['Expense', 'Transfer'] as const).map((k) => (
              <button key={k} type="button" role="radio" aria-checked={kind === k} className={kind === k ? 'on' : ''} onClick={() => setKind(k)}>
                {k === 'Transfer' ? 'Transfer between my accounts' : 'Expense'}
              </button>
            ))}
          </div>
        )}
        {kind === 'Transfer' && <p className="muted small">Transfers move your own money and never count as income or spending.</p>}
        {kind === 'Refund' && <p className="muted small">A refund reduces spending in the month it's received.</p>}

        <label className="field field-amount">
          <span>Amount ({me.data?.currency ?? 'PKR'})</span>
          <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} autoComplete="off" aria-invalid={amountError ? true : undefined} />
          {amountError && <small className="error-text">{amountError}</small>}
        </label>

        <div className="quick-row">
          <label className="field">
            <span>Date</span>
            <input
              type="date"
              value={date}
              min={MIN_DATE}
              max={kind === 'Refund' ? today : MAX_DATE}
              aria-invalid={dateError ? true : undefined}
              onChange={(e) => e.target.value && setDate(e.target.value)}
            />
            {dateError && <small className="error-text">{dateError}</small>}
          </label>
          {kind !== 'Refund' && (
            <label className="field">
              <span>Status</span>
              <select value={effectiveStatus} disabled={date > today} onChange={(e) => setStatus(e.target.value as TxStatus)}>
                <option value="Paid">Paid</option>
                <option value="Planned">Planned</option>
              </select>
            </label>
          )}
        </div>
        {date > today && kind !== 'Refund' && <p className="muted small">Future dates are planned until paid.</p>}

        {kind === 'Expense' && (
          <label className="field">
            <span>Category</span>
            <select value={categoryId ?? ''} onChange={(e) => setCategoryId(Number(e.target.value) || null)}>
              {spending.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                  {c.isArchived ? ' (archived)' : ''}
                </option>
              ))}
            </select>
          </label>
        )}

        {kind === 'Transfer' && (
          <div className="quick-row">
            <label className="field">
              <span>From</span>
              <select value={accountId ?? ''} onChange={(e) => setAccountId(Number(e.target.value) || null)}>
                <option value="">Default account</option>
                {(accounts.data ?? []).map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>To</span>
              <select value={toAccountId ?? ''} aria-invalid={sameAccount ? true : undefined} onChange={(e) => setToAccountId(Number(e.target.value) || null)}>
                <option value="">Choose…</option>
                {(accounts.data ?? []).map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))}
              </select>
              {sameAccount && <small className="error-text">Choose two different accounts</small>}
            </label>
          </div>
        )}

        <label className="field">
          <span>Description</span>
          <input value={description} maxLength={LIMITS.description} onChange={(e) => setDescription(e.target.value)} />
        </label>

        {kind !== 'Refund' && (
          <label className="field">
            <span>Payment method</span>
            <select value={method} onChange={(e) => setMethod(e.target.value)}>
              <option value="">—</option>
              {(me.data?.paymentMethods ?? []).map((m) => (
                <option key={m}>{m}</option>
              ))}
            </select>
          </label>
        )}

        {kind === 'Expense' && (
          <label className="toggle">
            <input type="checkbox" checked={additional} onChange={(e) => setAdditional(e.target.checked)} />
            <span>Additional / unexpected expense</span>
          </label>
        )}

        {custom.length > 0 && (
          <fieldset className="field custom-fields">
            <legend>Custom fields</legend>
            {customChecks.map(({ column: c, check }) => (
              <CustomInput
                key={c.key}
                column={c}
                value={values[c.key]}
                error={check.ok ? null : check.error}
                onChange={(v) => setValues({ ...values, [c.key]: v })}
              />
            ))}
          </fieldset>
        )}

        {row?.isRecurring && <p className="muted small">Created from a recurring item. Deleting it puts that occurrence back to planned.</p>}
        {error && <p className="error-text" role="alert">{error}</p>}
        {conflict && (
          <ConflictPanel
            current={conflict}
            mine={{ amount, date, categoryId: kind === 'Transfer' ? null : categoryId, description, status: effectiveStatus, method, additional: kind === 'Expense' && additional }}
            categories={categories.data ?? []}
            saving={update.isPending}
            onKeepMine={() => void save(conflict.version)}
            onUseTheirs={onClose}
          />
        )}

        <div className="form-actions">
          {row && (
            <>
              <button
                type="button"
                className="btn btn-danger-ghost"
                onClick={() => confirm('Delete this entry?') && remove.mutate({ id: row.id, version: row.version }, { onSuccess: onClose })}
              >
                Delete
              </button>
              {row.kind === 'Expense' && (
                <button type="button" className="btn" onClick={() => duplicate.mutate({ id: row.id }, { onSuccess: () => (toast('Duplicated', 'success'), onClose()) })}>
                  Duplicate
                </button>
              )}
              {row.kind === 'Expense' && row.status === 'Paid' && (
                <button type="button" className="btn" onClick={() => setRefunding(true)}>
                  Refund
                </button>
              )}
            </>
          )}
          <span className="spacer" />
          {/* While a conflict is open the choice is made in the panel, so Enter can't silently resubmit the old version. */}
          <button className="btn btn-primary" disabled={create.isPending || update.isPending || !!conflict || !!problem} title={problem ?? undefined}>
            {create.isPending || update.isPending ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </Sheet>
  );
}

function ConflictPanel({
  current,
  mine,
  categories,
  saving,
  onKeepMine,
  onUseTheirs,
}: {
  current: ExpenseRow;
  mine: EntryDraft;
  categories: Category[];
  saving: boolean;
  onKeepMine: () => void;
  onUseTheirs: () => void;
}) {
  const diffs = entryDiffs(current, mine, categories);
  return (
    <div className="warning-box conflict-box" role="alert">
      <p>
        <b>This entry was changed on another device</b> while you were editing it.
      </p>
      {diffs.length > 0 ? (
        <table className="conflict-table">
          <thead>
            <tr>
              <th scope="col">
                <span className="sr-only">Field</span>
              </th>
              <th scope="col">Yours</th>
              <th scope="col">Other device</th>
            </tr>
          </thead>
          <tbody>
            {diffs.map((d) => (
              <tr key={d.label}>
                <th scope="row">{d.label}</th>
                <td>{d.mine}</td>
                <td>{d.theirs}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="muted small">These fields match; something else (such as a custom field or the account) was changed.</p>
      )}
      <div className="btn-row">
        <button type="button" className="btn btn-primary" disabled={saving} onClick={onKeepMine}>
          {saving ? 'Saving…' : 'Keep my changes'}
        </button>
        <button type="button" className="btn" disabled={saving} onClick={onUseTheirs}>
          Use the other version
        </button>
      </div>
    </div>
  );
}

export function CustomInput({
  column,
  value,
  error,
  onChange,
}: {
  column: Column;
  value: unknown;
  error?: string | null;
  onChange: (v: unknown) => void;
}) {
  const label = <span>{column.name}</span>;
  const hint = error ? <small className="error-text">{error}</small> : null;
  const invalid = error ? true : undefined;
  switch (column.type) {
    case 'checkbox':
      return (
        <label className="toggle">
          <input type="checkbox" checked={value === true} onChange={(e) => onChange(e.target.checked)} />
          {label}
        </label>
      );
    case 'dropdown':
      return (
        <label className="field">
          {label}
          <select value={typeof value === 'string' ? value : ''} onChange={(e) => onChange(e.target.value || null)}>
            <option value="">—</option>
            {column.options.map((o) => (
              <option key={o}>{o}</option>
            ))}
          </select>
        </label>
      );
    case 'date':
      return (
        <label className="field">
          {label}
          <input type="date" value={typeof value === 'string' ? value : ''} max={MAX_DATE} aria-invalid={invalid} onChange={(e) => onChange(e.target.value || null)} />
          {hint}
        </label>
      );
    case 'money':
    case 'number':
      return (
        <label className="field">
          {label}
          <input
            inputMode="decimal"
            value={value == null ? '' : String(value)}
            aria-invalid={invalid}
            onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
          />
          {hint}
        </label>
      );
    default:
      return (
        <label className="field">
          {label}
          <input value={typeof value === 'string' ? value : ''} maxLength={LIMITS.description} aria-invalid={invalid} onChange={(e) => onChange(e.target.value || null)} />
          {hint}
        </label>
      );
  }
}

function RefundSheet({ row, onClose }: { row: ExpenseRow; onClose: () => void }) {
  const me = useMe();
  const today = useToday(me.data?.timeZone ?? 'Asia/Karachi');
  const refund = useRefund();
  const [amount, setAmount] = useState(String(row.amount - row.refundedAmount));
  const [date, setDate] = useState(today);
  const [description, setDescription] = useState(`Refund: ${row.description || row.categoryName}`);
  const [error, setError] = useState<string | null>(null);
  const [requestId] = useState(newRequestId);

  const left = Math.round((row.amount - row.refundedAmount) * 100) / 100;
  const amountCheck = checkAmount(amount);
  const amountError = amount.trim()
    ? firstError(amountCheck, amountCheck.ok && (amountCheck.value ?? 0) > left && `That's more than the ${money(left)} left to refund`)
    : null;
  const dateError = firstError(
    checkDate(date, {
      min: row.date,
      minMessage: "A refund can't be dated before the original expense",
      max: today,
      maxMessage: "A refund can't be dated after today",
    }),
  );

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const problem = firstError(amountCheck, amountError, dateError);
    if (problem || !amountCheck.ok || amountCheck.value == null) return setError(problem ?? 'Enter an amount');
    try {
      await refund.mutateAsync({ id: row.id, clientRequestId: requestId, date, amount: amountCheck.value, description });
      toast('Refund saved', 'success');
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save');
    }
  };

  return (
    <Sheet title="Refund" onClose={onClose}>
      <form className="form-stack" onSubmit={submit}>
        <p className="muted small">
          Original: {money(row.amount)} · {row.categoryName} on {row.date}
          {row.refundedAmount > 0 && ` · ${money(row.refundedAmount)} already refunded`}. The refund counts in the month you receive it; the original month stays as it was.
        </p>
        <label className="field field-amount">
          <span>Refund amount</span>
          <input inputMode="decimal" value={amount} aria-invalid={amountError ? true : undefined} onChange={(e) => setAmount(e.target.value)} />
          {amountError && <small className="error-text">{amountError}</small>}
        </label>
        <label className="field">
          <span>Received on</span>
          <input type="date" value={date} min={row.date} max={today} aria-invalid={dateError ? true : undefined} onChange={(e) => e.target.value && setDate(e.target.value)} />
          {dateError && <small className="error-text">{dateError}</small>}
        </label>
        <label className="field">
          <span>Note</span>
          <input value={description} maxLength={LIMITS.description} onChange={(e) => setDescription(e.target.value)} />
        </label>
        {error && <p className="error-text" role="alert">{error}</p>}
        <button className="btn btn-primary btn-block" disabled={refund.isPending || !amountCheck.ok || !!amountError || !!dateError}>
          <Icon name="income" size={16} /> Save refund
        </button>
      </form>
    </Sheet>
  );
}
