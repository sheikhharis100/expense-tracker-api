import { lazy, StrictMode, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import '@fontsource-variable/plus-jakarta-sans';
import { ApiError } from './api/client';
import { AuthProvider, useAuth } from './auth/AuthContext';
import { LiveProvider } from './live/LiveProvider';
import { AppShell } from './components/AppShell';
import { Toaster } from './components/Toast';
import { CurrentMonthRedirect, WorkspaceLayout } from './components/Workspace';
import { AuthPage } from './pages/AuthPage';
import { SummarySection } from './pages/SummarySection';
import { IncomeSection } from './pages/IncomeSection';
import { AdditionalSection, ExpensesSection } from './pages/ExpensesSection';
import './styles.css';

// The screens used every day load with the app; the rest come in their own files when first opened, so a phone
// downloads and starts less before showing this month. They're fetched in the background once the app is up, which
// also lets the service worker keep them for offline use.
const loadPlan = () => import('./pages/PlanSection');
const loadAnnual = () => import('./pages/AnnualPage');
const loadSettings = () => import('./pages/SettingsPage');
const loadProfile = () => import('./pages/ProfilePage');
const loadLinks = () => import('./pages/LinkPages');
const PlanSection = lazy(() => loadPlan().then((m) => ({ default: m.PlanSection })));
const AnnualPage = lazy(() => loadAnnual().then((m) => ({ default: m.AnnualPage })));
const SettingsPage = lazy(() => loadSettings().then((m) => ({ default: m.SettingsPage })));
const ProfilePage = lazy(() => loadProfile().then((m) => ({ default: m.ProfilePage })));
const ResetPasswordPage = lazy(() => loadLinks().then((m) => ({ default: m.ResetPasswordPage })));
const SignInLinkPage = lazy(() => loadLinks().then((m) => ({ default: m.SignInLinkPage })));
const VerifyEmailPage = lazy(() => loadLinks().then((m) => ({ default: m.VerifyEmailPage })));

// Pages opened from emailed links (kept in step with LinkPages).
const LINK_PATHS = ['/reset-password', '/sign-in-link', '/verify-email'];

function prefetchScreens() {
  const go = () => [loadPlan, loadAnnual, loadSettings, loadProfile, loadLinks].forEach((load) => load().catch(() => {}));
  if ('requestIdleCallback' in window) window.requestIdleCallback(go, { timeout: 5000 });
  else setTimeout(go, 2000);
}

const pageLoading = <div className="loading">Loading…</div>;

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      retry: (count, err) => !(err instanceof ApiError && err.status < 500) && count < 2,
    },
  },
});

function App() {
  const { token } = useAuth();
  const { pathname } = useLocation();

  // Links from emails open whether or not this device is signed in.
  if (LINK_PATHS.includes(pathname))
    return (
      <Suspense fallback={pageLoading}>
        <Routes>
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route path="/sign-in-link" element={<SignInLinkPage />} />
          <Route path="/verify-email" element={<VerifyEmailPage />} />
        </Routes>
      </Suspense>
    );
  if (!token) return <AuthPage />;

  return (
    <LiveProvider token={token}>
      <AppShell>
        <Suspense fallback={pageLoading}>
          <Routes>
          <Route path="/" element={<CurrentMonthRedirect />} />
          <Route path="/m/:ym" element={<WorkspaceLayout />}>
            <Route index element={<SummarySection />} />
            <Route path="income" element={<IncomeSection />} />
            <Route path="expenses" element={<ExpensesSection />} />
            <Route path="additional" element={<AdditionalSection />} />
            <Route path="plan" element={<PlanSection />} />
          </Route>
          <Route path="/year/:year" element={<AnnualPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
      </AppShell>
    </LiveProvider>
  );
}

// Lets the installed app open from cache while the free server wakes. Production only, so development always runs fresh code.
window.addEventListener('load', prefetchScreens);

if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Without it the app still works; it just needs the network to open.
    });
  });
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <App />
          <Toaster />
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
