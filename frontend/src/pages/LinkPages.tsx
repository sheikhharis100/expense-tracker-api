import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import type { AuthResponse } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../components/Icon';
import { confirmProblem, PasswordChecklist, PasswordField } from '../components/PasswordField';
import { toast } from '../components/Toast';
import { passwordProblem } from '../lib/password';
import { AuthCard, type AuthMode } from './AuthPage';

// Pages opened from emailed links. They work whether or not this device is signed in (see main.tsx).

export const LINK_PATHS = ['/reset-password', '/sign-in-link', '/verify-email'];

// Reads the code from the address bar once, then takes it out, so it doesn't stay in the history or get shared
// along with the page.
function useLinkCode(): string | null {
  const [code] = useState(() => new URLSearchParams(window.location.search).get('token'));
  const navigate = useNavigate();
  const { pathname, search } = useLocation();
  useEffect(() => {
    if (search) navigate(pathname, { replace: true });
  }, [navigate, pathname, search]);
  return code;
}

function NoCode() {
  return (
    <AuthCard title="This link isn't complete" intro="Open the newest link from your email again, or ask for a new one.">
      <Link to="/" className="btn btn-primary btn-block">
        Back to the app
      </Link>
    </AuthCard>
  );
}

// Sends people to the sign-in screen with the right form open.
function useAskAgain(mode: AuthMode) {
  const navigate = useNavigate();
  return () => navigate('/', { replace: true, state: { authMode: mode } });
}

export function ResetPasswordPage() {
  const code = useLinkCode();
  const { startSession } = useAuth();
  const navigate = useNavigate();
  const askAgain = useAskAgain('forgot');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);
  const [busy, setBusy] = useState(false);

  if (!code) return <NoCode />;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const problem = passwordProblem(password) ?? (confirm !== password ? "Passwords don't match." : null);
    setError(problem);
    if (problem) return;
    setBusy(true);
    try {
      const res = await api<AuthResponse>('/api/auth/reset-password', { method: 'POST', body: { token: code, newPassword: password }, auth: false });
      startSession(res);
      toast("Password changed. You're signed in, and your other devices were signed out.", 'success');
      navigate('/', { replace: true });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Something went wrong';
      setError(message);
      setExpired(/expired|already used/i.test(message));
    } finally {
      setBusy(false);
    }
  };

  const mismatch = confirmProblem(password, confirm);
  return (
    <AuthCard title="Choose a new password" intro="Pick something you don't use anywhere else. A short sentence with dashes works well.">
      <form className="auth-form" onSubmit={submit}>
        <PasswordField label="New password" value={password} onChange={setPassword} autoComplete="new-password" />
        <PasswordChecklist password={password} />
        <PasswordField label="Confirm new password" value={confirm} onChange={setConfirm} autoComplete="new-password" invalid={!!mismatch} />
        {mismatch && <p className="error-text">{mismatch}</p>}
        {error && (
          <p className="error-text" role="alert">
            {error}
          </p>
        )}
        {expired ? (
          <button type="button" className="btn btn-primary btn-block" onClick={askAgain}>
            Send me a new link
          </button>
        ) : (
          <button className="btn btn-primary btn-block" disabled={busy}>
            {busy ? 'Saving…' : 'Save password and sign in'}
          </button>
        )}
      </form>
    </AuthCard>
  );
}

export function SignInLinkPage() {
  const code = useLinkCode();
  const { startSession } = useAuth();
  const navigate = useNavigate();
  const askAgain = useAskAgain('link');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!code) return <NoCode />;

  // A tap rather than signing in on load: email apps and link scanners sometimes open links before you do.
  const signIn = async () => {
    setBusy(true);
    setError(null);
    try {
      startSession(await api<AuthResponse>('/api/auth/redeem-link', { method: 'POST', body: { token: code }, auth: false }));
      navigate('/', { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong');
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title="Sign in to Expense Tracker" intro="Tap the button to finish signing in on this device.">
      <div className="auth-form">
        {error && (
          <p className="error-text" role="alert">
            {error}
          </p>
        )}
        {error ? (
          <button type="button" className="btn btn-primary btn-block" onClick={askAgain}>
            Send me a new link
          </button>
        ) : (
          <button type="button" className="btn btn-primary btn-block" onClick={signIn} disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        )}
      </div>
    </AuthCard>
  );
}

export function VerifyEmailPage() {
  const code = useLinkCode();
  const { token } = useAuth();
  const qc = useQueryClient();
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);
  const started = useRef(false);

  useEffect(() => {
    // Once, even when React runs effects twice in development.
    if (!code || started.current) return;
    started.current = true;
    api<{ email: string }>('/api/auth/verify-email', { method: 'POST', body: { token: code }, auth: false })
      .then((res) => {
        setResult({ ok: true, message: `${res.email} is confirmed. Thanks!` });
        void qc.invalidateQueries({ queryKey: ['me'] });
      })
      .catch((err) => setResult({ ok: false, message: err instanceof Error ? err.message : 'Something went wrong' }));
  }, [code, qc]);

  if (!code) return <NoCode />;

  return (
    <AuthCard title={!result ? 'Confirming your email…' : result.ok ? 'Email confirmed' : "Couldn't confirm your email"}>
      {result && (
        <p className={result.ok ? 'auth-sent' : 'error-text'} role="status">
          {result.ok && <Icon name="check" size={20} />}
          {result.message}
        </p>
      )}
      <Link to={token ? '/profile' : '/'} className="btn btn-primary btn-block">
        {token ? 'Go to your profile' : 'Sign in'}
      </Link>
    </AuthCard>
  );
}
