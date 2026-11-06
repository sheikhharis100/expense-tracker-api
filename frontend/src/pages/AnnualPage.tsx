import { Link, Navigate, useParams } from 'react-router-dom';
import { useAnnual } from '../api/hooks';
import { getToken } from '../api/client';
import { ColumnChart } from '../components/ColumnChart';
import { Icon } from '../components/Icon';
import { useCalendar } from '../components/Workspace';
import { useSeriesColor } from '../lib/colors';
import { monthShort, parseYm } from '../lib/dates';
import { money, percent, signed } from '../lib/format';

export function AnnualPage() {
  const { year: param } = useParams();
  const year = Number(param);
  const { currentMonth } = useCalendar();
  const annual = useAnnual(year);
  const color = useSeriesColor();
  if (!Number.isInteger(year) || year < 2000 || year > 3000) return <Navigate to="/" replace />;
  const a = annual.data;

  const download = async (format: 'xlsx' | 'csv') => {
    const res = await fetch(`/api/exports/year/${year}?format=${format}`, { headers: { Authorization: `Bearer ${getToken()}` } });
    if (!res.ok) return;
    const link = document.createElement('a');
    link.href = URL.createObjectURL(await res.blob());
    link.download = `annual-${year}.${format}`;
    link.click();
  };

  return (
    <div className={`page ${annual.isPlaceholderData ? 'is-refetching' : ''}`}>
      <header className="page-header">
        <div>
          <p className="eyebrow">Annual summary · budget-month basis</p>
          <h1>{year}</h1>
        </div>
        <div className="page-header-actions">
          <Link className="btn btn-icon" to={`/year/${year - 1}`} aria-label="Previous year">
            <Icon name="left" />
          </Link>
          <Link className="btn btn-icon" to={`/year/${year + 1}`} aria-label="Next year">
            <Icon name="right" />
          </Link>
          <button type="button" className="btn btn-sm" onClick={() => download('xlsx')}>
            XLSX
          </button>
          <button type="button" className="btn btn-sm" onClick={() => download('csv')}>
            CSV
          </button>
        </div>
      </header>

      {!a && <div className="loading">Loading…</div>}
      {a && (
        <>
          <dl className="tiles">
            <div className="tile tile-income">
              <dt>Received income</dt>
              <dd>{money(a.totals.receivedIncome)}</dd>
            </div>
            <div className="tile tile-spent">
              <dt>Net spending</dt>
              <dd>{money(a.totals.netSpending)}</dd>
            </div>
            <div className="tile tile-safe">
              <dt>Surplus / deficit</dt>
              <dd className={a.totals.surplus < 0 ? 'negative' : ''}>{signed(a.totals.surplus, { dash: false })}</dd>
            </div>
            <div className="tile tile-avg">
              <dt>Income used</dt>
              <dd>{percent(a.totals.incomeUsedPercent)}</dd>
            </div>
          </dl>

          <section className="card">
            <div className="card-head">
              <h2>
                <Icon name="bars" className="card-icon" /> Net spending per month
              </h2>
            </div>
            <ColumnChart
              label={`Net spending per month in ${year}`}
              height={190}
              columns={a.rows.map((r) => ({
                key: r.month,
                tick: monthShort(parseYm(r.month)!.month),
                value: Math.max(0, r.netSpending),
                title: r.month,
                emphasis: r.month === currentMonth,
                muted: r.timing === 'future',
                rows: [
                  { label: 'Income', value: money(r.receivedIncome, { dash: false }) },
                  { label: 'Surplus', value: signed(r.surplus, { dash: false }) },
                ],
              }))}
            />
          </section>

          <section className="card card-flush">
            <div className="table-scroll">
              <table className="data-table annual-table">
                <thead>
                  <tr>
                    <th scope="col" className="sticky-col">Month</th>
                    <th scope="col" className="num">Expected salary</th>
                    <th scope="col" className="num">Salary income</th>
                    <th scope="col" className="num">Other income</th>
                    <th scope="col" className="num">Received income</th>
                    <th scope="col" className="num">Opening</th>
                    <th scope="col" className="num">Gross</th>
                    <th scope="col" className="num">Refunds</th>
                    <th scope="col" className="num">Net spending</th>
                    <th scope="col" className="num">Additional (incl.)</th>
                    <th scope="col" className="num">Surplus</th>
                    <th scope="col" className="num">Remaining</th>
                    <th scope="col" className="num">% used</th>
                    <th scope="col" className="num">Budget</th>
                    <th scope="col" className="num">Planned, unpaid</th>
                  </tr>
                </thead>
                <tbody>
                  {a.rows.map((r) => (
                    <tr key={r.month} className={r.month === currentMonth ? 'row-today' : r.timing === 'future' ? 'row-future' : ''}>
                      <th scope="row" className="sticky-col">
                        <Link className="month-link" to={`/m/${r.month}`}>
                          {monthShort(parseYm(r.month)!.month)}
                        </Link>
                      </th>
                      <td className="num calc">{money(r.expectedSalary)}</td>
                      <td className="num">{money(r.salaryIncome)}</td>
                      <td className="num">{money(r.otherIncome)}</td>
                      <td className="num strong">{money(r.receivedIncome)}</td>
                      <td className="num">{signed(r.openingBalance)}</td>
                      <td className="num">{money(r.grossSpending)}</td>
                      <td className="num">{money(r.refunds)}</td>
                      <td className="num strong">{money(r.netSpending)}</td>
                      <td className="num calc">{money(r.additionalNet)}</td>
                      <td className={`num ${r.surplus < 0 ? 'negative' : ''}`}>{signed(r.surplus)}</td>
                      <td className={`num strong ${r.remainingBalance < 0 ? 'negative' : ''}`}>{signed(r.remainingBalance)}</td>
                      <td className="num">{percent(r.incomeUsedPercent)}</td>
                      <td className="num calc">{r.totalBudget == null ? 'Not set' : money(r.totalBudget)}</td>
                      <td className="num calc">{money(r.unpaidCommitments)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <th scope="row" className="sticky-col">Total</th>
                    <td className="num">{money(a.totals.expectedSalary)}</td>
                    <td className="num">{money(a.totals.salaryIncome)}</td>
                    <td className="num">{money(a.totals.otherIncome)}</td>
                    <td className="num">{money(a.totals.receivedIncome)}</td>
                    <td className="num">{signed(a.totals.openingBalance)}</td>
                    <td className="num">{money(a.totals.grossSpending)}</td>
                    <td className="num">{money(a.totals.refunds)}</td>
                    <td className="num">{money(a.totals.netSpending)}</td>
                    <td className="num">{money(a.totals.additionalNet)}</td>
                    <td className="num">{signed(a.totals.surplus)}</td>
                    <td className="num">{signed(a.totals.remainingBalance)}</td>
                    <td className="num">{percent(a.totals.incomeUsedPercent)}</td>
                    <td />
                    <td className="num">{money(a.totals.unpaidCommitments)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </section>

          {a.categories.length > 0 && (
            <section className="card">
              <div className="card-head">
                <h2>
                  <Icon name="target" className="card-icon" /> Categories in {year}
                </h2>
              </div>
              <ul className="plain-list ranked">
                {a.categories.map((c) => (
                  <li key={c.categoryId}>
                    <span>
                      <span className="swatch" style={{ background: color(c.color) }} aria-hidden="true" />
                      {c.name}
                    </span>
                    <span className="num">{money(c.net)}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}
    </div>
  );
}
