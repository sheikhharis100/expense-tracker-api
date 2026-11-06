import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
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
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AppShell>
    </LiveProvider>
  );
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
