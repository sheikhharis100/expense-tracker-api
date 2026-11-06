import type { MonthFigures } from '../api/types';
import { money } from './format';

export type HealthLevel = 'healthy' | 'watch' | 'over' | 'empty' | 'plan' | 'salary';

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
> & { salaryOutstanding?: number };

// A plain-language read of the month from the server's figures: overspent, watch it, or healthy.
export function monthHealth(f: HealthInput): MonthHealth {
  const leftShare = f.availableFunds > 0 ? Math.max(0, Math.min(1, f.remainingBalance / f.availableFunds)) : 0;

  if (f.timing === 'future' && f.netSpending === 0) {
    return { level: 'plan', title: 'Planning', message: 'Upcoming month. Planned amounts are shown separately and never count as actual.', leftShare };
  }
  // Salary that's due but not recorded as received isn't money yet. When it's the reason the month looks empty or
  // overspent, say that (and the summary offers "Mark as received") instead of calling it overspending.
  const due = f.timing === 'future' ? 0 : (f.salaryOutstanding ?? 0);
  if (due > 0 && f.remainingBalance + due >= 0 && (f.remainingBalance < 0 || f.receivedIncome === 0)) {
    const after = f.remainingBalance + due;
    return {
      level: 'salary',
      title: 'Salary not recorded',
      message: `Your salary of ${money(due)} isn't marked as received yet, so it isn't counted. Once it is, you'll have ${money(after, { dash: false })} left.`,
      leftShare,
    };
  }
  if (f.availableFunds <= 0 && f.netSpending <= 0) {
    return { level: 'empty', title: 'Getting started', message: 'Record your salary payment and your first expense to see how the month is going.', leftShare };
  }
  if (f.remainingBalance < 0) {
    const stillShort = due > 0 ? ` Even with the ${money(due)} salary still due, you'd be ${money(-(f.remainingBalance + due))} short.` : '';
    return { level: 'over', title: 'Overspent', message: `You've spent ${money(f.deficit)} more than you have this month.${stillShort}`, leftShare };
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
