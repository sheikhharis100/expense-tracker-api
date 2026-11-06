import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useSummary } from '../api/hooks';
import type { MonthFigures, MonthSummary } from '../api/types';
import { ColumnChart } from '../components/ColumnChart';
import { Donut } from '../components/Donut';
import { Icon, type IconName } from '../components/Icon';
import { Meter } from '../components/Meter';
import { SectionTitle, useWorkspace } from '../components/Workspace';
import { useSeriesColor } from '../lib/colors';
import { dayLabel, ymLabel } from '../lib/dates';
import { money, notSet, orNA, percent, signed, signedChange } from '../lib/format';
import { monthHealth, type MonthHealth } from '../lib/health';
import { foldSeries } from '../lib/series';

export function SummarySection() {
  const { ym } = useWorkspace();
  const summary = useSummary(ym);
  const s = summary.data;

  return (
    <div className={`page ${summary.isPlaceholderData ? 'is-refetching' : ''}`}>
      <SectionTitle actions={<ExportMenu ym={ym} />}>Summary</SectionTitle>
      {summary.isError && <p className="error-text">Couldn't load the summary: {summary.error.message}</p>}
      {!s && !summary.isError && <div className="loading">Loading…</div>}
      {s && <SummaryBody s={s} />}
    </div>
  );
}

function SummaryBody({ s }: { s: MonthSummary }) {
  const f = s.figures;
  const health = monthHealth(f);
  return (
    <>
      <HealthHero f={f} currency={s.currency} health={health} />
      <KeyTiles f={f} />
      <div className="grid-2">
        <Statement f={f} currency={s.currency} />
        <div className="stack">
          {f.timing === 'current' && <Allowance f={f} />}
          <PlanCard f={f} />
          <Comparison s={s} />
        </div>
      </div>
      <BudgetTable f={f} />
      <div className="grid-2 grid-2-wide-right">
        <WhereItWent f={f} />
        <DailyChart f={f} today={s.today} />
      </div>
      <CashFlowCard s={s} />
    </>
  );
}

// ---------- Hero ----------

const HEALTH_ICON: Record<MonthHealth['level'], IconName> = { healthy: 'shield', watch: 'flame', over: 'alert', empty: 'sparkle', plan: 'calendar' };

function HealthHero({ f, currency, health }: { f: MonthFigures; currency: string; health: MonthHealth }) {
  const used = f.availableFunds > 0 ? Math.min(100, (f.netSpending / f.availableFunds) * 100) : 0;
  const r = 58;
  const c = 2 * Math.PI * r;
  return (
    <section className={`hero-card health-${health.level}`}>
      <div className="hero-main">
        <p className="hero-eyebrow">Remaining balance</p>
        <p className="hero-value">
          {signed(f.remainingBalance, { dash: false })}
          <span className="hero-currency">{currency}</span>
        </p>
        <p className="hero-sub">
          of {money(f.availableFunds, { dash: false })} available · income used {percent(f.incomeUsedPercent)}
        </p>
        <div className="hero-bar" role="meter" aria-label="Share of available funds spent" aria-valuenow={Math.round(used)} aria-valuemin={0} aria-valuemax={100}>
          <div style={{ width: `${used}%` }} />
        </div>
        <div className="health-line">
          <span className="health-pill">
            <Icon name={HEALTH_ICON[health.level]} size={16} />
            {health.title}
          </span>
          <span className="health-message">{health.message}</span>
        </div>
      </div>
      <div className="health-ring" aria-hidden="true">
        <svg viewBox="0 0 140 140" width="140" height="140">
          <circle cx="70" cy="70" r={r} className="ring-track" />
          <circle cx="70" cy="70" r={r} className="ring-fill" strokeDasharray={`${c * health.leftShare} ${c}`} transform="rotate(-90 70 70)" />
        </svg>
        <div className="ring-text">
          <b>{Math.round(health.leftShare * 100)}%</b>
          <span>left</span>
        </div>
      </div>
    </section>
  );
}

function KeyTiles({ f }: { f: MonthFigures }) {
  return (
    <dl className="tiles">
      <Tile icon="income" tone="income" label="Received income" value={money(f.receivedIncome)} hint={`salary ${money(f.salaryIncome)} · other ${money(f.otherIncome)}`} />
      <Tile icon="spent" tone="spent" label="Net spending" value={money(f.netSpending)} hint={f.refunds ? `after ${money(f.refunds)} refunds` : `${f.daysWithSpending} days with spending`} />
      <Tile icon="pulse" tone="avg" label="Average per day" value={orNA(f.averageDailySpending, (v) => money(Math.round(v)))} hint={f.elapsedDays ? `over ${f.elapsedDays} days` : 'not started'} />
      <Tile
        icon="coins"
        tone="safe"
        label={f.timing === 'current' ? 'Daily allowance' : 'Surplus / deficit'}
        value={f.timing === 'current' ? orNA(f.dailyAllowance, (v) => money(Math.floor(v), { dash: false })) : signed(f.surplus, { dash: false })}
        hint={f.timing === 'current' ? `${f.remainingDaysIncludingToday} days left incl. today` : 'income − net spending'}
        negative={f.timing !== 'current' && f.surplus < 0}
      />
      <Tile icon="target" tone="budget" label="Budget remaining" value={notSet(f.overallBudgetRemaining)} hint={f.totalBudget == null ? 'no budgets set' : `of ${money(f.totalBudget)}`} negative={(f.overallBudgetRemaining ?? 0) < 0} />
      <Tile icon="calendar" tone="days" label="Planned, unpaid" value={money(f.unpaidCommitments)} hint={`${f.commitments.length} item${f.commitments.length === 1 ? '' : 's'} this month`} />
    </dl>
  );
}

function Tile({ icon, tone, label, value, hint, negative }: { icon: IconName; tone: string; label: string; value: string; hint?: string; negative?: boolean }) {
  return (
    <div className={`tile tile-${tone}`}>
      <span className="tile-icon">
        <Icon name={icon} size={18} />
      </span>
      <dt>{label}</dt>
      <dd className={negative ? 'negative' : ''}>{value}</dd>
      {hint && <span className="tile-hint">{hint}</span>}
    </div>
  );
}

// ---------- Statement ----------

function Row({ label, value, strong, muted, sub, note }: { label: string; value: string; strong?: boolean; muted?: boolean; sub?: boolean; note?: string }) {
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

function Statement({ f, currency }: { f: MonthFigures; currency: string }) {
  const salary = f.fundingSalaries[0];
  const openingNote = f.openingSource === 'carryover' ? 'carried over from last month' : f.openingSource === 'manual' ? 'entered for this month' : undefined;
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="wallet" className="card-icon" /> Month statement
        </h2>
        <span className="muted small">{currency} · budget-month basis</span>
      </div>
      <table className="kv-table statement">
        <tbody>
          <tr className="group-head">
            <th colSpan={2}>Salary {salary ? `(${ymLabel(salary.earned)} salary)` : ''}</th>
          </tr>
          <Row label="Expected net salary" value={money(f.expectedSalary)} />
          <Row label="Salary received" value={money(f.salaryReceivedTowardFunding)} />
          {f.salaryOverpaid > 0 ? (
            <Row label="Overpaid" value={money(f.salaryOverpaid)} note="received more than expected" />
          ) : (
            <Row label="Salary still due" value={money(f.salaryOutstanding)} muted={f.salaryOutstanding === 0} note={f.salaryOutstanding ? 'not counted until received' : undefined} />
          )}
          <tr className="group-head">
            <th colSpan={2}>Money available</th>
          </tr>
          <Row label="Opening balance" value={signed(f.openingBalance)} note={openingNote} />
          <Row label="Salary income for this month" value={money(f.salaryIncome)} />
          <Row label="Other received income" value={money(f.otherIncome)} />
          {(f.otherIncomeSources ?? []).map((s) => (
            <Row key={s.categoryId ?? 'none'} label={s.name} value={money(s.amount)} sub />
          ))}
          <Row label="Total received income" value={money(f.receivedIncome)} strong />
          <Row label="Available funds" value={signed(f.availableFunds)} strong note="opening + received income" />
          <tr className="group-head">
            <th colSpan={2}>Spending</th>
          </tr>
          <Row label="Gross spending" value={money(f.grossSpending)} />
          <Row label="Refunds" value={f.refunds ? `−${money(f.refunds)}` : '–'} />
          <Row label="Net spending" value={money(f.netSpending)} strong />
          <Row label="Additional expenses" value={money(f.additionalNet)} muted note="already included above" />
          <Row
            label="Average spent per day"
            value={orNA(f.averageDailySpending, (v) => money(Math.round(v)))}
            note={f.elapsedDays ? `${money(f.netSpending, { dash: false })} over ${f.elapsedDays} day${f.elapsedDays === 1 ? '' : 's'} so far` : undefined}
          />
          <Row label="Days with spending logged" value={String(f.daysWithSpending)} note={f.elapsedDays ? `of ${f.elapsedDays} so far` : undefined} />
        </tbody>
        <tfoot>
          <Row label="Remaining balance" value={signed(f.remainingBalance, { dash: false })} strong />
          <Row label={f.surplus < 0 ? 'Monthly deficit' : 'Monthly surplus'} value={signed(f.surplus, { dash: false })} note="received income − net spending" />
          <Row label="Income used" value={percent(f.incomeUsedPercent)} />
        </tfoot>
      </table>
      <p className="muted small">
        <Link to="income" className="link">
          Salary & income details →
        </Link>
      </p>
    </section>
  );
}

function Allowance({ f }: { f: MonthFigures }) {
  const [open, setOpen] = useState(false);
  return (
    <section className="card card-accent">
      <div className="card-head">
        <h2>
          <Icon name="coins" className="card-icon" /> Daily allowance
        </h2>
        <button type="button" className="btn btn-sm" aria-expanded={open} onClick={() => setOpen(!open)}>
          How it's worked out
        </button>
      </div>
      <p className="big-number">
        {orNA(f.dailyAllowance, (v) => money(Math.floor(v), { dash: false }))}
        <span className="muted"> / day</span>
      </p>
      {f.deficit > 0 && (
        <p className="warning-text">
          <Icon name="alert" size={14} /> You're {money(f.deficit)} short this month, so there is no allowance until money comes in.
        </p>
      )}
      {open && (
        <div className="explain">
          <p>
            (remaining balance {signed(f.remainingBalance, { dash: false })} − unpaid planned {money(f.unpaidCommitments, { dash: false })} − reserved savings{' '}
            {money(f.reservedSavings, { dash: false })}) ÷ {f.remainingDaysIncludingToday} days left including today, never below zero.
          </p>
          <p className="muted small">
            It assumes every planned item this month gets paid at its planned amount and that salary still due doesn't arrive. Each planned item is
            counted once, even if it's also a recurring item. Set reserved savings under Budgets & Recurring.
          </p>
        </div>
      )}
    </section>
  );
}

function PlanCard({ f }: { f: MonthFigures }) {
  if (f.commitments.length === 0 && f.forecastIncome === 0) return null;
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="calendar" className="card-icon" /> Still planned
        </h2>
        <span className="badge badge-plan">plan, not actual</span>
      </div>
      {f.commitments.length > 0 && (
        <ul className="plain-list">
          {f.commitments.slice(0, 6).map((c) => (
            <li key={`${c.source}-${c.id}`}>
              <span>
                {c.name} <span className="muted small">· due {dayLabel(c.dueDate).day}</span>
              </span>
              <span className="num">{money(c.amount)}</span>
            </li>
          ))}
        </ul>
      )}
      <table className="kv-table">
        <tbody>
          <Row label="Unpaid planned spending" value={money(f.unpaidCommitments)} />
          <Row label="Income still expected" value={money(f.forecastIncome)} note="salary due + expected other income" />
          <Row label="Forecast remaining" value={signed(f.forecastRemaining, { dash: false })} strong note="if everything planned happens" />
        </tbody>
      </table>
    </section>
  );
}

function Comparison({ s }: { s: MonthSummary }) {
  const f = s.figures;
  const p = s.previous;
  const color = useSeriesColor();
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="pulse" className="card-icon" /> Compared with {ymLabel(p.month)}
        </h2>
      </div>
      <table className="kv-table">
        <tbody>
          <Row label="Net spending" value={`${signedChange(p.netSpendingChange)}${p.netSpendingChangePercent != null ? ` (${percent(p.netSpendingChangePercent)})` : ''}`} note={`was ${money(p.netSpending, { dash: false })}`} />
          <Row label="Received income" value={signedChange(p.receivedIncomeChange)} note={`was ${money(p.receivedIncome, { dash: false })}`} />
        </tbody>
      </table>
      {f.topCategories.length > 0 && (
        <>
          <h3 className="mini-head">Highest spending</h3>
          <ol className="plain-list ranked">
            {f.topCategories.map((c) => (
              <li key={c.categoryId}>
                <span>
                  <span className="swatch" style={{ background: color(c.color) }} aria-hidden="true" />
                  {c.name}
                </span>
                <span className="num">{money(c.net)}</span>
              </li>
            ))}
          </ol>
        </>
      )}
    </section>
  );
}

// ---------- Budgets & charts ----------

function BudgetTable({ f }: { f: MonthFigures }) {
  const color = useSeriesColor();
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="target" className="card-icon" /> Budget vs actual
        </h2>
        <Link to="plan" className="link">
          Set budgets →
        </Link>
      </div>
      {f.budgetCoverageWarning && (
        <p className="warning-text">
          <Icon name="alert" size={14} /> {money(f.unbudgetedSpending)} was spent in categories without a budget ({f.unbudgetedCategories.join(', ')}). The
          overall budget remaining still subtracts all spending.
        </p>
      )}
      <div className="table-scroll">
        <table className="data-table budget-table">
          <thead>
            <tr>
              <th scope="col">Category</th>
              <th scope="col" className="num">Budget</th>
              <th scope="col" className="num">Net spent</th>
              <th scope="col" className="num">Remaining</th>
              <th scope="col" className="meter-col"><span className="sr-only">Used</span></th>
            </tr>
          </thead>
          <tbody>
            {f.categories.map((c) => (
              <tr key={c.categoryId} className={c.isArchived ? 'row-muted' : ''}>
                <th scope="row">
                  <span className="swatch" style={{ background: color(c.color) }} aria-hidden="true" />
                  {c.name}
                  {c.isArchived && <span className="badge">archived</span>}
                </th>
                <td className={`num ${c.budget == null ? 'muted' : ''}`}>{c.budget == null ? 'Not set' : money(c.budget, { dash: false })}</td>
                <td className="num">{money(c.net)}</td>
                <td className={`num ${(c.budgetRemaining ?? 0) < 0 ? 'negative' : ''}`}>{c.budgetRemaining == null ? '' : signed(c.budgetRemaining, { dash: false })}</td>
                <td className="meter-col">{c.budget != null && c.budget > 0 && <Meter value={c.net} max={c.budget} color={color(c.color)} label={`${c.name} budget used`} />}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <th scope="row">Total</th>
              <td className="num">{f.totalBudget == null ? 'Not set' : money(f.totalBudget, { dash: false })}</td>
              <td className="num">{money(f.netSpending)}</td>
              <td className={`num ${(f.overallBudgetRemaining ?? 0) < 0 ? 'negative' : ''}`}>{f.overallBudgetRemaining == null ? '' : signed(f.overallBudgetRemaining, { dash: false })}</td>
              <td />
            </tr>
            {f.groups.map((g) => (
              <tr key={g.name} className="group-row">
                <th scope="row">{g.name} total</th>
                <td className="num">{g.budget == null ? '' : money(g.budget)}</td>
                <td className="num">{money(g.net)}</td>
                <td className="num">{g.budget == null ? '' : signed(g.budget - g.net)}</td>
                <td />
              </tr>
            ))}
          </tfoot>
        </table>
      </div>
    </section>
  );
}

function WhereItWent({ f }: { f: MonthFigures }) {
  const color = useSeriesColor();
  const refs = f.categories.map((c) => ({ id: c.categoryId, name: c.name, type: 'Expense' as const, color: c.color, groupName: c.group, isArchived: c.isArchived }));
  const totals = Object.fromEntries(f.categories.map((c) => [c.categoryId, c.net]));
  const slices = foldSeries(refs, totals, color)
    .map((s) => ({ key: s.key, label: s.name, color: s.color, value: s.ids.reduce((sum, id) => sum + (totals[id] ?? 0), 0) }))
    .filter((s) => s.value > 0)
    .sort((a, b) => b.value - a.value);
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="target" className="card-icon" /> Where it went
        </h2>
      </div>
      {slices.length === 0 ? (
        <p className="empty-note">
          <Icon name="leaf" size={16} /> No spending recorded.
        </p>
      ) : (
        <Donut slices={slices} total={slices.reduce((a, s) => a + s.value, 0)} centerLabel="net spent" />
      )}
    </section>
  );
}

function DailyChart({ f, today }: { f: MonthFigures; today: string }) {
  const color = useSeriesColor();
  const refs = f.categories.map((c) => ({ id: c.categoryId, name: c.name, type: 'Expense' as const, color: c.color, groupName: c.group, isArchived: c.isArchived }));
  const totals = Object.fromEntries(f.categories.map((c) => [c.categoryId, c.net]));
  const series = foldSeries(refs, totals, color);
  const columns = f.days.map((d) => {
    const { day, weekday, dayNumber } = dayLabel(d.date);
    const isToday = d.date === today;
    return {
      key: d.date,
      tick: isToday ? 'Today' : dayNumber === 1 || dayNumber % 5 === 0 ? String(dayNumber) : '',
      value: Math.max(0, d.net),
      title: `${weekday} ${day}`,
      segments: series.map((s) => ({ key: s.key, label: s.name, color: s.color, value: Math.max(0, s.ids.reduce((sum, id) => sum + (d.byCategory[id] ?? 0), 0)) })),
      emphasis: isToday,
      muted: d.date > today,
    };
  });
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="bars" className="card-icon" /> Spending per day
        </h2>
        <Link to="expenses" className="link">
          Daily log →
        </Link>
      </div>
      {series.length > 1 && (
        <ul className="chart-legend" aria-label="Legend">
          {series.map((s) => (
            <li key={s.key}>
              <span className="legend-key" style={{ background: s.color }} aria-hidden="true" />
              {s.name}
            </li>
          ))}
        </ul>
      )}
      <ColumnChart
        columns={columns}
        label={`Net spending per day in ${ymLabel(f.month)}`}
        reference={f.averageDailySpending ? { value: f.averageDailySpending, label: `avg ${money(Math.round(f.averageDailySpending))}/day` } : undefined}
      />
    </section>
  );
}

function CashFlowCard({ s }: { s: MonthSummary }) {
  const c = s.cashFlow;
  return (
    <section className="card">
      <div className="card-head">
        <h2>
          <Icon name="pulse" className="card-icon" /> Cash flow
        </h2>
        <span className="badge">receipt-date basis</span>
      </div>
      <p className="muted small">
        Money by the day it actually arrived, whatever month it funds. This is a separate view: it's never added to the budget-month figures above.
      </p>
      <table className="kv-table">
        <tbody>
          <Row label="Salary received in this month" value={money(c.salaryReceived)} />
          <Row label="Other income received in this month" value={money(c.otherReceived)} />
          <Row label="Net spending" value={money(c.netSpending)} />
          <Row label="Net cash flow" value={signed(c.netCashFlow, { dash: false })} strong />
        </tbody>
      </table>
    </section>
  );
}

function ExportMenu({ ym }: { ym: string }) {
  const download = async (url: string, name: string) => {
    const { getToken } = await import('../api/client');
    const res = await fetch(url, { headers: { Authorization: `Bearer ${getToken()}` } });
    if (!res.ok) return;
    const blob = await res.blob();
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    a.click();
    URL.revokeObjectURL(a.href);
  };
  return (
    <div className="btn-group">
      <button type="button" className="btn btn-sm" onClick={() => download(`/api/exports/month/${ym}?format=xlsx`, `expenses-${ym}.xlsx`)}>
        <Icon name="upload" size={14} /> XLSX
      </button>
      <button type="button" className="btn btn-sm" onClick={() => download(`/api/exports/month/${ym}?format=csv&part=entries`, `entries-${ym}.csv`)}>
        CSV
      </button>
    </div>
  );
}
