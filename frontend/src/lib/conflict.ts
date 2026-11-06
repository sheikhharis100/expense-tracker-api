import type { Category, ExpenseRow, TxStatus } from '../api/types';
import { parseAmounts } from './amounts';
import { dayLabel } from './dates';
import { money } from './format';

// What the entry form would save, as typed.
export interface EntryDraft {
  amount: string;
  date: string;
  categoryId: number | null;
  description: string;
  status: TxStatus;
  method: string;
  additional: boolean;
}

export interface FieldDiff {
  label: string;
  mine: string;
  theirs: string;
}

// A 409's `current` should be the server's copy of the row; anything else falls back to a plain message.
export const isExpenseRow = (v: unknown): v is ExpenseRow =>
  !!v &&
  typeof v === 'object' &&
  typeof (v as ExpenseRow).version === 'number' &&
  typeof (v as ExpenseRow).amount === 'number' &&
  typeof (v as ExpenseRow).date === 'string';

// Field by field: what this form would save next to what the other device saved. Only differences are listed.
export function entryDiffs(current: ExpenseRow, mine: EntryDraft, categories: Pick<Category, 'id' | 'name'>[]): FieldDiff[] {
  const parsed = parseAmounts(mine.amount);
  const myAmount = parsed.ok && parsed.amounts.length === 1 ? parsed.amounts[0] : null;
  const categoryName = (id: number | null) => (id == null ? '—' : (categories.find((c) => c.id === id)?.name ?? `#${id}`));
  const text = (v: string | null | undefined) => v?.trim() || '(empty)';
  const diffs: FieldDiff[] = [];
  const add = (label: string, same: boolean, mineText: string, theirsText: string) => {
    if (!same) diffs.push({ label, mine: mineText, theirs: theirsText });
  };

  add('Amount', myAmount === current.amount, myAmount == null ? text(mine.amount) : money(myAmount, { dash: false }), money(current.amount, { dash: false }));
  add('Date', mine.date === current.date, dayLabel(mine.date).day, dayLabel(current.date).day);
  add('Category', mine.categoryId === current.categoryId, categoryName(mine.categoryId), current.categoryName || categoryName(current.categoryId));
  add('Description', text(mine.description) === text(current.description), text(mine.description), text(current.description));
  add('Status', mine.status === current.status, mine.status, current.status);
  add('Payment method', (mine.method || '') === (current.paymentMethod || ''), mine.method || '—', current.paymentMethod || '—');
  add('Additional', mine.additional === current.isAdditional, mine.additional ? 'Yes' : 'No', current.isAdditional ? 'Yes' : 'No');
  return diffs;
}
