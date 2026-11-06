import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { NavLink, useMatch } from 'react-router-dom';
import { useTheme } from '../lib/theme';
import { useLive } from '../live/LiveProvider';
import { ColumnEditorProvider } from './ColumnEditor';
import { Icon } from './Icon';
import { SyncBadge, SyncSheet, useOfflineSync, WakingBanner } from './OfflineSync';
import { QuickAddProvider } from './QuickAdd';
import { useCalendar } from './Workspace';

function LiveBadge() {
  const { status, remoteChangeAt } = useLive();
  const [flash, setFlash] = useState(false);

  useEffect(() => {
    if (!remoteChangeAt) return;
    setFlash(true);
    const t = window.setTimeout(() => setFlash(false), 2500);
    return () => window.clearTimeout(t);
  }, [remoteChangeAt]);

  const text = flash ? 'Synced' : status === 'live' ? 'Live' : status === 'connecting' ? 'Connecting…' : 'Offline';
  return (
    <span
      className={`live-badge live-${status} ${flash ? 'live-flash' : ''}`}
      title={status === 'live' ? 'Changes from your other devices appear here instantly' : 'Reconnecting; your changes still save'}
    >
      <span className="live-dot" aria-hidden="true" />
      <span className="live-text">{text}</span>
    </span>
  );
}

// One tap between light and dark. Settings → Appearance can hand it back to the device setting.
function ThemeToggle() {
  const { theme, setChoice } = useTheme();
  const next = theme === 'dark' ? 'light' : 'dark';
  return (
    <button type="button" className="btn btn-ghost btn-icon theme-toggle" onClick={() => setChoice(next)} aria-label={`Switch to the ${next} theme`} title={`Switch to the ${next} theme`}>
      <Icon name={theme === 'dark' ? 'sun' : 'moon'} />
    </button>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { currentMonth } = useCalendar();
  const monthMatch = useMatch('/m/:ym/*');
  const yearMatch = useMatch('/year/:year');
  const viewedYear = monthMatch?.params.ym?.slice(0, 4) ?? yearMatch?.params.year ?? currentMonth.slice(0, 4);
  const [syncOpen, setSyncOpen] = useState(false);
  const closeSync = useCallback(() => setSyncOpen(false), []);
  useOfflineSync();

  return (
    <ColumnEditorProvider>
      <QuickAddProvider>
        <div className="shell">
          <header className="topbar">
            <div className="topbar-inner">
              <NavLink to="/" className="brand">
                <span className="brand-mark" aria-hidden="true" />
                <span className="brand-name">Expense Tracker</span>
              </NavLink>
              <nav className="nav" aria-label="Main">
                <NavLink to={`/m/${monthMatch?.params.ym ?? currentMonth}`} className={() => `nav-link ${monthMatch ? 'active' : ''}`}>
                  <Icon name="calendar" className="nav-icon" />
                  <span className="nav-text">Month</span>
                </NavLink>
                <NavLink to={`/year/${viewedYear}`} className="nav-link">
                  <Icon name="bars" className="nav-icon" />
                  <span className="nav-text">Year</span>
                </NavLink>
                <NavLink to="/settings" className="nav-link">
                  <Icon name="sliders" className="nav-icon" />
                  <span className="nav-text">Settings</span>
                </NavLink>
              </nav>
              <SyncBadge onOpen={() => setSyncOpen(true)} />
              <LiveBadge />
              <ThemeToggle />
            </div>
            <WakingBanner />
          </header>
          <main className="main">{children}</main>
          {/* Outside the header: its backdrop blur would trap the fixed-position sheet. */}
          {syncOpen && <SyncSheet onClose={closeSync} />}
        </div>
      </QuickAddProvider>
    </ColumnEditorProvider>
  );
}
