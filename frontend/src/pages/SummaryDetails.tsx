import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { MonthFigures } from '../api/types';
import { Icon } from '../components/Icon';
import { Meter } from '../components/Meter';
import { Sheet } from '../components/Sheet';
import { useSeriesColor } from '../lib/colors';
import { dayLabel, ymLabel, ymLongLabel } from '../lib/dates';
import { money, orNA, percent, signed } from '../lib/format';

// Every figure at the top of the Summary opens one of these: how it's made up, and where to see or change it.
export type Kpi = 'balance' | 'income' | 'spending' | 'average' | 'allowance' | 'budget' | 'planned';

export function Row({ label, value, strong, muted, sub, note }: { label: ReactNode; value: string; strong?: boolean; muted?: boolean; sub?: boolean; note?: string }) {
  return (
    <tr className={`${strong ? 'row-strong' : ''} ${muted ? 'row-muted' : ''} ${sub ? 'row-sub' : ''}`}>
      <th scope="row">
        {label}
        {note && <span className="row-note">{note}</span>}
      </th>
      <td className="num">{value}</td>
    </tr>
  );
}

// The ledger opened with filters already applied (read by ExpensesSection).
export function ledgerLink(filter: { category?: number; status?: 'Paid' | 'Planned'; additional?: boolean } = {}) {
  const params = new URLSearchParams({ view: 'ledger' });
  if (filter.category) params.set('category', String(filter.category));
  if (filter.status) params.set('status', filter.status);
  if (filter.additional) params.set('additional', '1');
  return `expenses?${params}`;
}

const TITLES: Record<Kpi, string> = {
  balance: 'Remaining balance',
  income: 'Received income',
  spending: 'Net spending',
  average: 'Average per day',
  allowance: 'Daily allowance',
  budget: 'Budget remaining',
  planned: 'Planned, unpaid',
};

export function KpiSheet({ kpi, f, onClose, onShowStatement }: { kpi: Kpi; f: MonthFigures; onClose: () => void; onShowStatement: () => void }) {
  const title = kpi === 'allowance' && f.timing !== 'current' ? 'Surplus / deficit' : TITLES[kpi];
  return (
    <Sheet title={`${title} · ${ymLabel(f.month)}`} onClose={onClose} wide>
      <div className="kpi-detail">
        {kpi === 'balance' && <BalanceDetail f={f} onShowStatement={onShowStatement} />}
        {kpi === 'income' && <IncomeDetail f={f} />}
        {kpi === 'spending' && <SpendingDetail f={f} />}
        {kpi === 'average' && <AverageDetail f={f} />}
        {kpi === 'allowance' && <AllowanceDetail f={f} />}
        {kpi === 'budget' && <BudgetDetail f={f} />}
        {kpi === 'planned' && <PlannedDetail f={f} />}
      </div>
    </Sheet>
  );
}

function Actions({ children }: { children: ReactNode }) {
  return <div className="form-actions kpi-actions">{children}</div>;
}

function BalanceDetail({ f, onShowStatement }: { f: MonthFigures; onShowStatement: () => void }) {
  return (
    <>
      <table className="kv-table statement">
        <tbody>
          <Row label="Opening balance" value={signed(f.openingBalance)} note={f.openingSource === 'carryover' ? 'carried over from last month' : undefined} />
          <Row label="+ Received income" value={money(f.receivedIncome)} />
          <Row label="= Available funds" value={signed(f.availableFunds)} strong />
          <Row label="− Net spending" value={money(f.netSpending)} />
        </tbody>
        <tfoot>
          <Row label="= Remaining balance" value={signed(f.remainingBalance, { dash: false })} strong />
          <Row label="Income used" value={percent(f.incomeUsedPercent)} />
        </tfoot>
      </table>
      <Actions>
        <button type="button" className="btn btn-primary" onClick={onShowStatement}>
          <Icon name="wallet" size={16} /> Full month statement
        </button>
      </Actions>
    </>
  );
}

function IncomeDetail({ f }: { f: MonthFigures }) {
  const salary = f.fundingSalaries[0];
  return (
    <>
      <table className="kv-table statement">
        <tbody>
          <Row label={salary ? `Salary (${ymLongLabel(salary.earned)})` : 'Salary'} value={money(f.salaryIncome)} note={salary ? `funds ${ymLongLabel(f.month)}` : 'no salary for this month yet'} />
          <Row label="Other income" value={money(f.otherIncome)} />
          {(f.otherIncomeSources ?? []).map((s) => (
            <Row key={s.categoryId ?? 'none'} label={s.name} value={money(s.amount)} sub />
          ))}
        </tbody>
        <tfoot>
          <Row label="Total received" value={money(f.receivedIncome)} strong />
          {f.salaryOutstanding > 0 && <Row label="Salary still due" value={money(f.salaryOutstanding)} muted note="not counted until it's received" />}
          {f.expectedOtherIncome > 0 && <Row label="Other income expected" value={money(f.expectedOtherIncome)} muted note="not counted until it's received" />}
        </tfoot>
      </table>
      <Actions>
        <Link to="income" className="btn btn-primary">
          <Icon name="wallet" size={16} /> Open Salary & Income
        </Link>
      </Actions>
    </>
  );
}

function SpendingDetail({ f }: { f: MonthFigures }) {
  const color = useSeriesColor();
  const spent = f.categories.filter((c) => c.gross !== 0 || c.refunds !== 0).sort((a, b) => b.net - a.net);
  return (
    <>
      <table className="kv-table statement">
        <tbody>
          <Row label="Gross spending" value={money(f.grossSpending)} />
          <Row label="− Refunds" value={f.refunds ? money(f.refunds) : '–'} />
        </tbody>
        <tfoot>
          <Row label="= Net spending" value={money(f.netSpending)} strong />
          <Row label="of which additional / unexpected" value={money(f.additionalNet)} muted />
        </tfoot>
      </table>
      {spent.length > 0 ? (
        <>
          <h3 className="mini-head">By category · tap one to see its entries</h3>
          <ul className="kpi-list">
            {spent.map((c) => (
              <li key={c.categoryId}>
                <Link to={ledgerLink({ category: c.categoryId })} className="kpi-list-link">
                  <span>
                    <span className="swatch" style={{ background: color(c.color) }} aria-hidden="true" />
                    {c.name}
                  </span>
                  <span className="num">
                    {money(c.net)} <span className="muted small">{f.netSpending > 0 ? percent((c.net / f.netSpending) * 100) : ''}</span>
                  </span>
                  <Icon name="right" size={14} className="kpi-chevron" />
                </Link>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <p className="muted">Nothing spent yet this month.</p>
      )}
      <Actions>
        <Link to={ledgerLink()} className="btn btn-primary">
          <Icon name="grid" size={16} /> All entries
        </Link>
        <Link to="expenses?view=matrix" className="btn">
          Daily log
        </Link>
        {f.additionalNet > 0 && (
          <Link to="additional" className="btn">
            Additional expenses
          </Link>
        )}
      </Actions>
    </>
  );
}

function AverageDetail({ f }: { f: MonthFigures }) {
  const spentDays = f.days.filter((d) => d.net !== 0);
  const highest = spentDays.reduce<(typeof spentDays)[number] | null>((top, d) => (!top || d.net > top.net ? d : top), null);
  return (
    <>
      <table className="kv-table statement">
        <tbody>
          <Row label="Net spending" value={money(f.netSpending)} />
          <Row label="÷ Days so far" value={f.elapsedDays ? String(f.elapsedDays) : '–'} note={f.timing === 'current' ? 'including today' : undefined} />
        </tbody>
        <tfoot>
          <Row label="= Average per day" value={orNA(f.averageDailySpending, (v) => money(Math.round(v)))} strong />
          <Row label="Days with spending logged" value={`${f.daysWithSpending}${f.elapsedDays ? ` of ${f.elapsedDays}` : ''}`} />
          {highest && <Row label="Highest day" value={money(highest.net)} note={dayLabel(highest.date).day} />}
        </tfoot>
      </table>
      {spentDays.length > 0 && (
        <>
          <h3 className="mini-head">Each day</h3>
          <ul className="kpi-list kpi-days">
            {spentDays.map((d) => (
              <li key={d.date}>
                <span>
                  {dayLabel(d.date).day} <span className="muted small">{dayLabel(d.date).weekday}</span>
                </span>
                <span className="kpi-day-bar" aria-hidden="true">
                  <span style={{ width: `${highest ? Math.max(4, (d.net / highest.net) * 100) : 0}%` }} />
                </span>
                <span className="num">{money(d.net)}</span>
              </li>
            ))}
          </ul>
        </>
      )}
      <Actions>
        <Link to="expenses?view=matrix" className="btn btn-primary">
          <Icon name="grid" size={16} /> Open the daily log
        </Link>
      </Actions>
    </>
  );
}

function AllowanceDetail({ f }: { f: MonthFigures }) {
  if (f.timing !== 'current')
    return (
      <>
        <table className="kv-table statement">
          <tbody>
            <Row label="Received income" value={money(f.receivedIncome)} />
            <Row label="− Net spending" value={money(f.netSpending)} />
          </tbody>
          <tfoot>
            <Row label={f.surplus < 0 ? '= Deficit' : '= Surplus'} value={signed(f.surplus, { dash: false })} strong />
          </tfoot>
        </table>
        <p className="muted small">The daily allowance is only worked out for the current month.</p>
      </>
    );
  const spendable = f.remainingBalance - f.unpaidCommitments - f.reservedSavings;
  return (
    <>
      <table className="kv-table statement">
        <tbody>
          <Row label="Remaining balance" value={signed(f.remainingBalance, { dash: false })} />
          <Row label="− Planned, not paid yet" value={money(f.unpaidCommitments, { dash: false })} />
          <Row label="− Reserved savings" value={money(f.reservedSavings, { dash: false })} />
          <Row label="= Free to spend" value={signed(spendable, { dash: false })} strong />
          <Row label="÷ Days left" value={String(f.remainingDaysIncludingToday ?? '–')} note="including today" />
        </tbody>
        <tfoot>
          <Row label="= Daily allowance" value={orNA(f.dailyAllowance, (v) => money(Math.floor(v), { dash: false }))} strong note="never below zero" />
        </tfoot>
      </table>
      {f.deficit > 0 && (
        <p className="warning-text">
          <Icon name="alert" size={14} /> You're {money(f.deficit)} short this month, so there's no allowance until money comes in.
        </p>
      )}
      <p className="muted small">
        It assumes every planned item gets paid at its planned amount and that salary still due doesn't arrive. Reserved savings are set under Budgets &
        Recurring.
      </p>
      <Actions>
        <Link to="plan" className="btn btn-primary">
          <Icon name="target" size={16} /> Budgets & Recurring
        </Link>
        <Link to="income" className="btn">
          Record income
        </Link>
      </Actions>
    </>
  );
}

function BudgetDetail({ f }: { f: MonthFigures }) {
  const color = useSeriesColor();
  const budgeted = f.categories.filter((c) => c.budget != null);
  if (budgeted.length === 0)
    return (
      <>
        <p>No budgets are set for {ymLongLabel(f.month)} yet. Give each category a monthly limit and this shows how much of it is left.</p>
        <Actions>
          <Link to="plan" className="btn btn-primary">
            <Icon name="target" size={16} /> Set budgets
          </Link>
        </Actions>
      </>
    );
  return (
    <>
      <ul className="kpi-list">
        {budgeted.map((c) => (
          <li key={c.categoryId} className="kpi-budget">
            <Link to={ledgerLink({ category: c.categoryId })} className="kpi-list-link">
              <span>
                <span className="swatch" style={{ background: color(c.color) }} aria-hidden="true" />
                {c.name}
              </span>
              <span className={`num ${(c.budgetRemaining ?? 0) < 0 ? 'negative' : ''}`}>
                {signed(c.budgetRemaining ?? 0, { dash: false })} <span className="muted small">left of {money(c.budget ?? 0, { dash: false })}</span>
              </span>
              <Icon name="right" size={14} className="kpi-chevron" />
            </Link>
            {(c.budget ?? 0) > 0 && <Meter value={c.net} max={c.budget ?? 0} color={color(c.color)} label={`${c.name} budget used`} />}
          </li>
        ))}
      </ul>
      <table className="kv-table statement">
        <tfoot>
          <Row label="Total budget" value={money(f.totalBudget ?? 0, { dash: false })} />
          <Row label="− Net spending (all categories)" value={money(f.netSpending)} />
          <Row label="= Budget remaining" value={signed(f.overallBudgetRemaining ?? 0, { dash: false })} strong />
        </tfoot>
      </table>
      {f.budgetCoverageWarning && (
        <p className="warning-text">
          <Icon name="alert" size={14} /> {money(f.unbudgetedSpending)} was spent without a budget ({f.unbudgetedCategories.join(', ')}).
        </p>
      )}
      <Actions>
        <Link to="plan" className="btn btn-primary">
          <Icon name="target" size={16} /> Change budgets
        </Link>
      </Actions>
    </>
  );
}

function PlannedDetail({ f }: { f: MonthFigures }) {
  return (
    <>
      {f.commitments.length === 0 ? (
        <p>Nothing planned and unpaid for {ymLongLabel(f.month)}. Planned entries and recurring bills show here until they're paid.</p>
      ) : (
        <ul className="kpi-list">
          {f.commitments.map((c) => (
            <li key={`${c.source}-${c.id}`}>
              <span>
                {c.name}
                <span className="muted small">
                  {' '}
                  · due {dayLabel(c.dueDate).day} · {c.source === 'recurring' ? 'recurring' : 'planned entry'}
                </span>
              </span>
              <span className="num">{money(c.amount)}</span>
            </li>
          ))}
        </ul>
      )}
      <table className="kv-table statement">
        <tfoot>
          <Row label="Unpaid planned spending" value={money(f.unpaidCommitments)} strong />
          <Row label="Income still expected" value={money(f.forecastIncome)} note="salary due + expected other income" />
          <Row label="Forecast remaining" value={signed(f.forecastRemaining, { dash: false })} note="if everything planned happens" />
        </tfoot>
      </table>
      <Actions>
        <Link to="plan" className="btn btn-primary">
          <Icon name="calendar" size={16} /> Budgets & Recurring
        </Link>
        <Link to={ledgerLink({ status: 'Planned' })} className="btn">
          Planned entries
        </Link>
      </Actions>
    </>
  );
}
