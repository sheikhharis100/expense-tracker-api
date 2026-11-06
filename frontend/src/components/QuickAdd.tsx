import { createContext, useContext, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useMatch } from 'react-router-dom';
import { newRequestId, useCategories, useMe } from '../api/hooks';
import { queuedMessage, useCreateOrQueue } from '../api/offline';
import type { TxStatus } from '../api/types';
import { parseAmounts } from '../lib/amounts';
import { useSeriesColor } from '../lib/colors';
import { lastDayOf, monthOfIso, useToday } from '../lib/dates';
import { money } from '../lib/format';
import { checkDate, firstError, MAX_DATE, MIN_DATE } from '../lib/validate';
import { Icon } from './Icon';
import { Sheet } from './Sheet';
import { toast } from './Toast';

interface QuickAddApi {
  open: (preset?: { date?: string; categoryId?: number; isAdditional?: boolean }) => void;
}

const QuickAddContext = createContext<QuickAddApi>({ open: () => {} });
export const useQuickAdd = () => useContext(QuickAddContext);

const LAST_CATEGORY_KEY = 'expense-tracker.last-category';
const LAST_METHOD_KEY = 'expense-tracker.last-method';

function remember(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // A convenience only.
  }
}

function recall(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

// The floating "+" on every screen: the fastest way to log a spend on a phone.
export function QuickAddProvider({ children }: { children: ReactNode }) {
  const [preset, setPreset] = useState<Parameters<QuickAddApi['open']>[0] | null>(null);
  const monthMatch = useMatch('/m/:ym/*');
  // Load categories with the app, so the device copy exists before the phone ever goes offline.
  useCategories();

  return (
    <QuickAddContext.Provider value={{ open: (p) => setPreset(p ?? {}) }}>
      {children}
      <button type="button" className="fab" onClick={() => setPreset({})} aria-label="Add an expense">
        <Icon name="plus" size={26} />
      </button>
      {preset && <QuickAddSheet preset={preset} viewedMonth={monthMatch?.params.ym} onClose={() => setPreset(null)} />}
    </QuickAddContext.Provider>
  );
}

function QuickAddSheet({
  preset,
  viewedMonth,
  onClose,
}: {
  preset: NonNullable<Parameters<QuickAddApi['open']>[0]>;
  viewedMonth?: string;
  onClose: () => void;
}) {
  const me = useMe();
  const today = useToday(me.data?.timeZone ?? 'Asia/Karachi');
  const categories = useCategories();
  const create = useCreateOrQueue();
  const color = useSeriesColor();
  const amountRef = useRef<HTMLInputElement>(null);

  // Default to today, or the last day of a past month being viewed / the first of a future one.
  const defaultDate = useMemo(() => {
    if (preset.date) return preset.date;
    if (!viewedMonth || viewedMonth === monthOfIso(today)) return today;
    return viewedMonth < monthOfIso(today) ? lastDayOf(viewedMonth) : `${viewedMonth}-01`;
  }, [preset.date, viewedMonth, today]);

  const spending = (categories.data ?? []).filter((c) => c.type === 'Expense' && !c.isArchived).sort((a, b) => a.sortOrder - b.sortOrder);
  const remembered = Number(recall(LAST_CATEGORY_KEY));
  const [categoryId, setCategoryId] = useState<number | null>(preset.categoryId ?? (Number.isFinite(remembered) ? remembered : null));
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(defaultDate);
  const [description, setDescription] = useState('');
  const [method, setMethod] = useState(recall(LAST_METHOD_KEY) ?? 'Cash');
  const [additional, setAdditional] = useState(preset.isAdditional ?? false);
  const [plannedChoice, setPlannedChoice] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // One id per entry the user is composing, reused on retries so a flaky connection can't double-save.
  const requestIds = useRef<string[]>([]);

  const selected = spending.find((c) => c.id === categoryId) ?? spending[0];
  const isFuture = date > today;
  const status: TxStatus = isFuture || plannedChoice ? 'Planned' : 'Paid';
  // Shown as you type; an empty amount only complains on save.
  const amountError = amount.trim() ? firstError(parseAmounts(amount)) : null;
  const dateError = firstError(checkDate(date));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!selected || amountError || dateError) return;
    const parsed = parseAmounts(amount);
    if (!parsed.ok || parsed.amounts.length === 0) {
      setError(parsed.ok ? 'Enter an amount' : parsed.error);
      return;
    }
    setError(null);
    while (requestIds.current.length < parsed.amounts.length) requestIds.current.push(newRequestId());
    try {
      let queued = 0;
      for (const [i, value] of parsed.amounts.entries()) {
        const input = {
          clientRequestId: requestIds.current[i],
          date,
          amount: value,
          kind: 'Expense' as const,
          status,
          categoryId: selected.id,
          description,
          paymentMethod: method,
          isAdditional: additional,
        };
        // Once one couldn't reach the server, keep the rest on the phone too rather than waiting on each.
        if (queued) create.queue(input);
        else if ((await create.save(input)) === 'saved') continue;
        queued++;
      }
      remember(LAST_CATEGORY_KEY, String(selected.id));
      remember(LAST_METHOD_KEY, method);
      if (queued) toast(queuedMessage(), 'info');
      else toast(`${status === 'Planned' ? 'Planned' : 'Saved'} ${money(parsed.amounts.reduce((a, b) => a + b, 0))} · ${selected.name}`, 'success');
      requestIds.current = [];
      setAmount('');
      setDescription('');
      setAdditional(false);
      amountRef.current?.focus();
    } catch (err) {
      // Nothing is cleared: the entry stays on screen to retry.
      setError(err instanceof Error ? err.message : 'Could not save');
    }
  };

  return (
    <Sheet title="Add expense" onClose={onClose}>
      <form className="quick-add" onSubmit={submit}>
        <label className="field field-amount">
          <span>Amount ({me.data?.currency ?? 'PKR'})</span>
          <input
            ref={amountRef}
            inputMode="decimal"
            enterKeyHint="done"
            placeholder="0"
            value={amount}
            onChange={(e) => {
              setAmount(e.target.value);
              requestIds.current = [];
            }}
            aria-invalid={error || amountError ? true : undefined}
            aria-describedby="quick-amount-hint"
            autoComplete="off"
          />
          {amountError ? (
            <small id="quick-amount-hint" className="error-text">{amountError}</small>
          ) : (
            <small id="quick-amount-hint" className="muted">Tip: 100+50 saves two entries.</small>
          )}
        </label>

        <div className="chips" role="radiogroup" aria-label="Category">
          {spending.map((c) => (
            <button
              key={c.id}
              type="button"
              role="radio"
              aria-checked={selected?.id === c.id}
              className={`chip ${selected?.id === c.id ? 'chip-on' : ''}`}
              style={{ ['--chip' as string]: color(c.color) }}
              onClick={() => setCategoryId(c.id)}
            >
              <span className="swatch" style={{ background: color(c.color) }} aria-hidden="true" />
              {c.name}
            </button>
          ))}
        </div>

        <div className="quick-row">
          <label className="field">
            <span>Date</span>
            <input
              type="date"
              value={date}
              min={MIN_DATE}
              max={MAX_DATE}
              aria-invalid={dateError ? true : undefined}
              onChange={(e) => e.target.value && setDate(e.target.value)}
            />
            {dateError && <small className="error-text">{dateError}</small>}
          </label>
          <label className="field">
            <span>Paid with</span>
            <select value={method} onChange={(e) => setMethod(e.target.value)}>
              {(me.data?.paymentMethods ?? ['Cash']).map((m) => (
                <option key={m}>{m}</option>
              ))}
            </select>
          </label>
        </div>

        <label className="field">
          <span>Note (optional)</span>
          <input placeholder="e.g. groceries" value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
        </label>

        <div className="toggles">
          <label className="toggle">
            <input type="checkbox" checked={additional} onChange={(e) => setAdditional(e.target.checked)} />
            <span>Additional / unexpected</span>
          </label>
          <label className={`toggle ${isFuture ? 'disabled' : ''}`}>
            <input type="checkbox" checked={isFuture || plannedChoice} disabled={isFuture} onChange={(e) => setPlannedChoice(e.target.checked)} />
            <span>Planned, not paid yet{isFuture ? ' (future date)' : ''}</span>
          </label>
        </div>

        {error && <p className="error-text" role="alert">{error}</p>}
        <button className="btn btn-primary btn-block btn-lg" disabled={!selected || create.isPending || !!amountError || !!dateError}>
          {create.isPending ? 'Saving…' : status === 'Planned' ? `Plan for ${selected?.name ?? ''}` : `Save to ${selected?.name ?? ''}`}
        </button>
      </form>
    </Sheet>
  );
}
