import { useState, type FormEvent } from 'react';
import {
  newRequestId,
  useAccounts,
  useCategories,
  useColumns,
  useCreateExpense,
  useDeleteExpense,
  useDuplicateExpense,
  useMe,
  useRefund,
  useUpdateExpense,
} from '../api/hooks';
import { ApiError } from '../api/client';
import type { Column, ExpenseRow, TxKind, TxStatus } from '../api/types';
import { parseAmounts } from '../lib/amounts';
import { useToday } from '../lib/dates';
import { money } from '../lib/format';
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
  const create = useCreateExpense();
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

  const effectiveStatus: TxStatus = kind === 'Refund' ? 'Paid' : date > today ? 'Planned' : status;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseAmounts(amount);
    if (!parsed.ok || parsed.amounts.length !== 1) return setError(parsed.ok ? 'Enter one amount' : parsed.error);
    const input = {
      date,
      amount: parsed.amounts[0],
      kind,
      status: effectiveStatus,
      categoryId: kind === 'Transfer' ? null : categoryId,
      description,
      paymentMethod: method,
      isAdditional: kind === 'Expense' ? additional : false,
      accountId,
      transferAccountId: kind === 'Transfer' ? toAccountId : null,
      refundOfId: row?.refundOfId ?? null,
      custom: values,
    };
    try {
      if (row) await update.mutateAsync({ ...input, id: row.id, version: row.version });
      else await create.mutateAsync({ ...input, clientRequestId: requestId });
      toast('Saved', 'success');
      onClose();
    } catch (err) {
      // Entered values stay put; a conflict explains that someone else changed it first.
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
          <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} autoComplete="off" />
        </label>

        <div className="quick-row">
          <label className="field">
            <span>Date</span>
            <input type="date" value={date} onChange={(e) => e.target.value && setDate(e.target.value)} />
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
              <select value={toAccountId ?? ''} onChange={(e) => setToAccountId(Number(e.target.value) || null)}>
                <option value="">Choose…</option>
                {(accounts.data ?? []).map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
        )}

        <label className="field">
          <span>Description</span>
          <input value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
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
            {custom.map((c) => (
              <CustomInput key={c.key} column={c} value={values[c.key]} onChange={(v) => setValues({ ...values, [c.key]: v })} />
            ))}
          </fieldset>
        )}

        {row?.isRecurring && <p className="muted small">Created from a recurring item. Deleting it puts that occurrence back to planned.</p>}
        {error && <p className="error-text" role="alert">{error}</p>}

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
          <button className="btn btn-primary" disabled={create.isPending || update.isPending}>
            {create.isPending || update.isPending ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </Sheet>
  );
}

export function CustomInput({ column, value, onChange }: { column: Column; value: unknown; onChange: (v: unknown) => void }) {
  const label = <span>{column.name}</span>;
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
          <input type="date" value={typeof value === 'string' ? value : ''} onChange={(e) => onChange(e.target.value || null)} />
        </label>
      );
    case 'money':
    case 'number':
      return (
        <label className="field">
          {label}
          <input inputMode="decimal" value={value == null ? '' : String(value)} onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)} />
        </label>
      );
    default:
      return (
        <label className="field">
          {label}
          <input value={typeof value === 'string' ? value : ''} maxLength={500} onChange={(e) => onChange(e.target.value || null)} />
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

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseAmounts(amount);
    if (!parsed.ok || parsed.amounts.length !== 1) return setError('Enter one amount');
    try {
      await refund.mutateAsync({ id: row.id, clientRequestId: requestId, date, amount: parsed.amounts[0], description });
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
          <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </label>
        <label className="field">
          <span>Received on</span>
          <input type="date" value={date} min={row.date} max={today} onChange={(e) => e.target.value && setDate(e.target.value)} />
        </label>
        <label className="field">
          <span>Note</span>
          <input value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
        </label>
        {error && <p className="error-text" role="alert">{error}</p>}
        <button className="btn btn-primary btn-block" disabled={refund.isPending}>
          <Icon name="income" size={16} /> Save refund
        </button>
      </form>
    </Sheet>
  );
}
