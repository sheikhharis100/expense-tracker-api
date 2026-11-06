import type { MonthFigures } from '../api/types';
import { money } from './format';

export type HealthLevel = 'healthy' | 'watch' | 'over' | 'empty' | 'plan';

export interface MonthHealth {
  level: HealthLevel;
  title: string;
  message: string;
  // Share of available funds still left, 0–1.
  leftShare: number;
}

const LOW_LEFT_SHARE = 0.15;

type HealthInput = Pick<
  MonthFigures,
  'timing' | 'availableFunds' | 'netSpending' | 'remainingBalance' | 'dailyAllowance' | 'categories' | 'receivedIncome' | 'deficit'
>;

// A plain-language read of the month from the server's figures: overspent, watch it, or healthy.
export function monthHealth(f: HealthInput): MonthHealth {
  const leftShare = f.availableFunds > 0 ? Math.max(0, Math.min(1, f.remainingBalance / f.availableFunds)) : 0;

  if (f.timing === 'future' && f.netSpending === 0) {
    return { level: 'plan', title: 'Planning', message: 'Upcoming month. Planned amounts are shown separately and never count as actual.', leftShare };
  }
  if (f.availableFunds <= 0 && f.netSpending <= 0) {
    return { level: 'empty', title: 'Getting started', message: 'Record your salary payment and your first expense to see how the month is going.', leftShare };
  }
  if (f.remainingBalance < 0) {
    return { level: 'over', title: 'Overspent', message: `You've spent ${money(f.deficit)} more than you have this month.`, leftShare };
  }

  const over = f.categories.filter((c) => c.isOverBudget).map((c) => c.name);
  if (over.length > 0) {
    const names = over.length <= 2 ? over.join(' and ') : `${over.slice(0, 2).join(', ')} and ${over.length - 2} more`;
    return { level: 'watch', title: 'Watch it', message: `${names} ${over.length === 1 ? 'is' : 'are'} over budget.`, leftShare };
  }
  if (leftShare < LOW_LEFT_SHARE) {
    return { level: 'watch', title: 'Watch it', message: `Only ${Math.round(leftShare * 100)}% of this month's money is left.`, leftShare };
  }
  return {
    level: 'healthy',
    title: 'Healthy',
    message:
      f.timing === 'current' && f.dailyAllowance != null
        ? `You can spend about ${money(Math.floor(f.dailyAllowance))} a day and still cover what's planned.`
        : `${Math.round(leftShare * 100)}% of the month's money was left.`,
    leftShare,
  };
}
