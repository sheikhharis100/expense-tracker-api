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
  it("says the salary isn't recorded instead of overspent when that's the only reason", () => {
    // The user's November: 60,500 salary expected but not marked received, 14,920 spent.
    const h = monthHealth({ ...base, availableFunds: 0, receivedIncome: 0, netSpending: 14920, remainingBalance: -14920, deficit: 14920, salaryOutstanding: 60500 });
    expect(h.level).toBe('salary');
    expect(h.message).toContain('45,580');
  });

  it('still says overspent when even the salary due would not cover it', () => {
    const h = monthHealth({ ...base, availableFunds: 0, receivedIncome: 0, netSpending: 70000, remainingBalance: -70000, deficit: 70000, salaryOutstanding: 60500 });
    expect(h.level).toBe('over');
    expect(h.message).toContain('9,500');
  });

  it('prompts to record a salary that is due before anything is spent', () => {
    expect(monthHealth({ ...base, availableFunds: 0, receivedIncome: 0, netSpending: 0, remainingBalance: 0, salaryOutstanding: 55000 }).level).toBe('salary');
  });

  it('ignores salary due in a future month', () => {
    expect(monthHealth({ ...base, timing: 'future', netSpending: 0, salaryOutstanding: 55000 }).level).toBe('plan');
  });

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
