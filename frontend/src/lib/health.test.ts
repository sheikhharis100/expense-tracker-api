import { describe, expect, it } from 'vitest';
import { monthHealth } from './health';

const base = {
  timing: 'current' as const,
  availableFunds: 60000,
  netSpending: 30000,
  remainingBalance: 30000,
  dailyAllowance: 1500,
  receivedIncome: 60000,
  deficit: 0,
  categories: [],
};
const cat = (name: string, isOverBudget: boolean) => ({
  categoryId: 1, name, color: '#2a78d6', group: null, isArchived: false, gross: 0, refunds: 0, net: 0,
  budget: 100, budgetRemaining: 0, percentOfBudget: 0, isOverBudget,
});

describe('monthHealth', () => {
  it('is healthy with money left and no broken budgets', () => {
    const h = monthHealth(base);
    expect(h.level).toBe('healthy');
    expect(h.message).toContain('1,500 a day');
    expect(h.leftShare).toBeCloseTo(0.5, 2);
  });

  it('asks to watch when a budget is broken', () => {
    const h = monthHealth({ ...base, categories: [cat('Food', false), cat('Other Expenses', true)] });
    expect(h.level).toBe('watch');
    expect(h.message).toBe('Other Expenses is over budget.');
  });

  it('asks to watch when little money is left', () => {
    expect(monthHealth({ ...base, remainingBalance: 3000, netSpending: 57000 }).level).toBe('watch');
  });

  it('flags overspending with the deficit', () => {
    const h = monthHealth({ ...base, remainingBalance: -2000, netSpending: 62000, deficit: 2000 });
    expect(h.level).toBe('over');
    expect(h.message).toContain('2,000');
  });

  it('is empty before any money is entered', () => {
    expect(monthHealth({ ...base, availableFunds: 0, netSpending: 0, remainingBalance: 0 }).level).toBe('empty');
  });

  it('treats untouched future months as plans', () => {
    expect(monthHealth({ ...base, timing: 'future', netSpending: 0 }).level).toBe('plan');
  });
});
