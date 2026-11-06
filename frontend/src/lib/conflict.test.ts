import { describe, expect, it } from 'vitest';
import type { ExpenseRow } from '../api/types';
import { entryDiffs, isExpenseRow, type EntryDraft } from './conflict';

const server: ExpenseRow = {
  id: 7,
  version: 4,
  date: '2026-11-05',
  amount: 1200,
  kind: 'Expense',
  status: 'Paid',
  categoryId: 2,
  categoryName: 'Food',
  categoryColor: '#2a78d6',
  description: 'Lunch',
  paymentMethod: 'Cash',
  isAdditional: false,
  isRecurring: false,
  recurringOccurrenceId: null,
  refundOfId: null,
  refundedAmount: 0,
  accountId: 1,
  accountName: 'Wallet',
  transferAccountId: null,
  transferAccountName: null,
  custom: {},
  computed: {},
};

const same: EntryDraft = { amount: '1200', date: '2026-11-05', categoryId: 2, description: 'Lunch', status: 'Paid', method: 'Cash', additional: false };
const categories = [
  { id: 2, name: 'Food' },
  { id: 3, name: 'Fuel' },
];

describe('entryDiffs', () => {
  it('lists nothing when the typed values match the server', () => {
    expect(entryDiffs(server, same, categories)).toEqual([]);
  });

  it('lists each differing field with both values', () => {
    const diffs = entryDiffs(server, { ...same, amount: '1500', categoryId: 3, method: 'Card', additional: true }, categories);
    expect(diffs).toEqual([
      { label: 'Amount', mine: '1,500', theirs: '1,200' },
      { label: 'Category', mine: 'Fuel', theirs: 'Food' },
      { label: 'Payment method', mine: 'Card', theirs: 'Cash' },
      { label: 'Additional', mine: 'Yes', theirs: 'No' },
    ]);
  });

  it('shows dates, blank descriptions and status readably', () => {
    const diffs = entryDiffs(server, { ...same, date: '2026-11-06', description: '  ', status: 'Planned' }, categories);
    expect(diffs.map((d) => [d.label, d.mine, d.theirs])).toEqual([
      ['Date', '06 Nov', '05 Nov'],
      ['Description', '(empty)', 'Lunch'],
      ['Status', 'Planned', 'Paid'],
    ]);
  });

  it('shows an amount that is not a single number as typed', () => {
    expect(entryDiffs(server, { ...same, amount: '100+50' }, categories)[0]).toEqual({ label: 'Amount', mine: '100+50', theirs: '1,200' });
  });
});

describe('isExpenseRow', () => {
  it('accepts a server row and rejects anything else', () => {
    expect(isExpenseRow(server)).toBe(true);
    expect(isExpenseRow(undefined)).toBe(false);
    expect(isExpenseRow({ valueCount: 3 })).toBe(false);
  });
});
