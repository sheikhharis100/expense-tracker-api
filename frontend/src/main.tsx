import { StrictMode } from 'react';
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
import { PlanSection } from './pages/PlanSection';
import { AnnualPage } from './pages/AnnualPage';
import { SettingsPage } from './pages/SettingsPage';
import { ProfilePage } from './pages/ProfilePage';
import { LINK_PATHS, ResetPasswordPage, SignInLinkPage, VerifyEmailPage } from './pages/LinkPages';
import './styles.css';

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
      <Routes>
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/sign-in-link" element={<SignInLinkPage />} />
        <Route path="/verify-email" element={<VerifyEmailPage />} />
      </Routes>
    );
  if (!token) return <AuthPage />;

  return (
    <LiveProvider token={token}>
      <AppShell>
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
      </AppShell>
    </LiveProvider>
  );
}

// Lets the installed app open from cache while the free server wakes. Production only, so development always runs fresh code.
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
