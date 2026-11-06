import { createContext, useContext, useEffect, useRef, type ReactNode } from 'react';
import { NavLink, Navigate, Outlet, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useMe } from '../api/hooks';
import type { Me, Timing } from '../api/types';
import { monthOfIso, monthShort, parseYm, useToday, ymKey, ymLongLabel } from '../lib/dates';
import { Icon, type IconName } from './Icon';

interface WorkspaceState {
  me: Me;
  today: string;
  ym: string;
  currentMonth: string;
  timing: Timing;
}

const WorkspaceContext = createContext<WorkspaceState | null>(null);

export function useWorkspace(): WorkspaceState {
  const ctx = useContext(WorkspaceContext);
  if (!ctx) throw new Error('useWorkspace outside workspace');
  return ctx;
}

export function timingOf(ym: string, currentMonth: string): Timing {
  return ym < currentMonth ? 'past' : ym === currentMonth ? 'current' : 'future';
}

// Today and the current month, from the user's zone. Available on every signed-in screen.
export function useCalendar() {
  const me = useMe();
  const today = useToday(me.data?.timeZone ?? 'Asia/Karachi');
  return { me: me.data, today, currentMonth: monthOfIso(today) };
}

export const SECTIONS: { path: string; label: string; short: string; icon: IconName }[] = [
  { path: '', label: 'Summary', short: 'Summary', icon: 'home' },
  { path: 'income', label: 'Salary & Income', short: 'Income', icon: 'wallet' },
  { path: 'expenses', label: 'Expenses / Daily log', short: 'Expenses', icon: 'grid' },
  { path: 'additional', label: 'Additional expenses', short: 'Additional', icon: 'sparkle' },
  { path: 'plan', label: 'Budgets & Recurring', short: 'Plan', icon: 'target' },
];

export function CurrentMonthRedirect() {
  const { me, currentMonth } = useCalendar();
  if (!me) return <div className="loading">Loading…</div>;
  return <Navigate to={`/m/${currentMonth}`} replace />;
}

export function WorkspaceLayout() {
  const { ym = '' } = useParams();
  const { me, today, currentMonth } = useCalendar();
  if (!parseYm(ym)) return <Navigate to="/" replace />;
  if (!me) return <div className="loading">Loading…</div>;

  const state: WorkspaceState = { me, today, ym, currentMonth, timing: timingOf(ym, currentMonth) };
  return (
    <WorkspaceContext.Provider value={state}>
      <div className="workspace">
        <MonthTabs ym={ym} currentMonth={currentMonth} />
        <SectionTabs ym={ym} />
        {state.timing === 'future' && (
          <div className="plan-banner" role="note">
            <Icon name="calendar" size={16} />
            <span>
              <b>{ymLongLabel(ym)} is in the future.</b> You can plan here; planned amounts never count as actual income or spending until
              they're received or paid.
            </span>
          </div>
        )}
        <Outlet />
      </div>
    </WorkspaceContext.Provider>
  );
}

function MonthTabs({ ym, currentMonth }: { ym: string; currentMonth: string }) {
  const navigate = useNavigate();
  const section = useLocation().pathname.split('/')[3] ?? '';
  const year = parseYm(ym)!.year;
  const listRef = useRef<HTMLDivElement>(null);
  const go = (key: string) => navigate(`/m/${key}${section ? `/${section}` : ''}`);

  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>('[aria-current="page"]')?.scrollIntoView({ inline: 'center', block: 'nearest' });
  }, [ym]);

  return (
    <div className="month-bar">
      <div className="year-picker" role="group" aria-label="Year">
        <button type="button" className="btn btn-icon" aria-label="Previous year" onClick={() => go(ymKey({ year: year - 1, month: parseYm(ym)!.month }))}>
          <Icon name="left" />
        </button>
        <NavLink to={`/year/${year}`} className="year-label" title="Year summary">
          {year}
        </NavLink>
        <button type="button" className="btn btn-icon" aria-label="Next year" onClick={() => go(ymKey({ year: year + 1, month: parseYm(ym)!.month }))}>
          <Icon name="right" />
        </button>
      </div>
      <div className="month-tabs" role="tablist" aria-label={`Months of ${year}`} ref={listRef}>
        {Array.from({ length: 12 }, (_, i) => {
          const key = ymKey({ year, month: i + 1 });
          const timing = timingOf(key, currentMonth);
          return (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={key === ym}
              aria-current={key === ym ? 'page' : undefined}
              className={`month-tab month-${timing} ${key === ym ? 'on' : ''}`}
              onClick={() => go(key)}
              title={`${ymLongLabel(key)} · ${timing}`}
            >
              {monthShort(i + 1)}
              <span className="month-dot" aria-hidden="true" />
              <span className="sr-only">{timing === 'current' ? '(this month)' : `(${timing})`}</span>
            </button>
          );
        })}
      </div>
      {ym !== currentMonth && (
        <button type="button" className="btn btn-sm today-btn" onClick={() => go(currentMonth)}>
          This month
        </button>
      )}
    </div>
  );
}

function SectionTabs({ ym }: { ym: string }) {
  return (
    <nav className="section-tabs" aria-label="Month sections">
      {SECTIONS.map((s) => (
        <NavLink key={s.path} to={`/m/${ym}${s.path ? `/${s.path}` : ''}`} end className="section-tab">
          <Icon name={s.icon} size={16} />
          <span className="section-long">{s.label}</span>
          <span className="section-short">{s.short}</span>
        </NavLink>
      ))}
    </nav>
  );
}

export function SectionTitle({ children, actions }: { children: ReactNode; actions?: ReactNode }) {
  const { ym, timing } = useWorkspace();
  return (
    <header className="page-header">
      <div>
        <p className="eyebrow">
          {ymLongLabel(ym)} · <span className={`timing-${timing}`}>{timing === 'current' ? 'This month' : timing === 'past' ? 'Past month' : 'Upcoming'}</span>
        </p>
        <h1>{children}</h1>
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </header>
  );
}
