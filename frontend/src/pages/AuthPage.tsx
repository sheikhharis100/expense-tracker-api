import { useState, type FormEvent, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { api } from '../api/client';
import { useAuthOptions } from '../api/hooks';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../components/Icon';
import { confirmProblem, PasswordChecklist, PasswordField } from '../components/PasswordField';
import { passwordProblem } from '../lib/password';

export type AuthMode = 'signin' | 'register' | 'forgot' | 'link';

// The layout of every signed-out screen, including the pages opened from emailed links: a branded panel
// (beside the form on a computer, a banner above it on a phone) and the form card.
export function AuthCard({ title, intro, children }: { title: string; intro?: ReactNode; children: ReactNode }) {
  return (
    <div className="auth">
      <div className="auth-shell">
        <aside className="auth-hero">
          <div className="brand auth-hero-brand">
            <span className="brand-mark" aria-hidden="true" />
            Expense Tracker
          </div>
          <p className="auth-hero-title">Every rupee, month by month.</p>
          <p className="auth-hero-text">Log spending in seconds on your phone. Your salary funds the next month and the summary keeps itself up to date.</p>
          <ul className="auth-points">
            <li>
              <Icon name="calendar" size={16} /> Salary and spending, month by month
            </li>
            <li>
              <Icon name="pulse" size={16} /> Live on your phone and computer
            </li>
            <li>
              <Icon name="shield" size={16} /> Private to you, and works offline
            </li>
          </ul>
          <div className="auth-preview" aria-hidden="true">
            <span className="auth-preview-label">Example month</span>
            <span className="auth-preview-value">
              27,600 <small>PKR left</small>
            </span>
            <span className="auth-preview-bar">
              <span style={{ width: '54%' }} />
            </span>
            <span className="auth-preview-note">54% of income used · Healthy</span>
          </div>
        </aside>
        <div className="auth-card card">
          <h1>{title}</h1>
          {intro && <p className="muted">{intro}</p>}
          {children}
        </div>
      </div>
    </div>
  );
}

const TITLES: Record<AuthMode, string> = {
  signin: 'Sign in',
  register: 'Create your tracker',
  forgot: 'Reset your password',
  link: 'Sign in with email',
};

const INTROS: Record<AuthMode, string> = {
  signin: 'Welcome back. Sign in to see this month.',
  register: 'Free, and takes a minute. Your entries stay private to your account.',
  forgot: "Enter the email you signed up with and we'll send you a link to choose a new password.",
  link: "We'll email you a link that signs you in. No password needed.",
};

export function AuthPage() {
  const { signIn, register } = useAuth();
  const options = useAuthOptions();
  const emailEnabled = options.data?.emailEnabled ?? false;
  // A link page can send people here with a mode picked ("Ask for a new link").
  const requested = (useLocation().state as { authMode?: AuthMode } | null)?.authMode;
  const [mode, setMode] = useState<AuthMode>(requested ?? 'signin');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const go = (next: AuthMode) => {
    setMode(next);
    setError(null);
    setSent(null);
    setPassword('');
    setConfirm('');
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (mode === 'register') {
      const problem = passwordProblem(password, email) ?? (confirm !== password ? "Passwords don't match." : null);
      if (problem) return setError(problem);
    }
    setBusy(true);
    try {
      if (mode === 'signin') await signIn(email, password);
      else if (mode === 'register') await register(email, password, displayName);
      else {
        const url = mode === 'forgot' ? '/api/auth/forgot-password' : '/api/auth/email-link';
        const res = await api<{ message: string }>(url, { method: 'POST', body: { email }, auth: false });
        setSent(res.message);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong');
    } finally {
      setBusy(false);
    }
  };

  const needsEmail = mode === 'forgot' || mode === 'link';
  const mismatch = mode === 'register' ? confirmProblem(password, confirm) : null;

  return (
    <AuthCard title={TITLES[mode]} intro={INTROS[mode]}>
      {needsEmail && sent ? (
        <div className="auth-sent" role="status">
          <Icon name="mail" size={22} />
          <p>{sent}</p>
          <p className="muted small">Nothing after a few minutes? Check the address and try again; links can be sent once a minute.</p>
        </div>
      ) : needsEmail && options.isSuccess && !emailEnabled ? (
        <p className="warning-text">
          <Icon name="alert" size={16} />
          <span>Email isn't set up on this server yet, so links can't be sent. Ask whoever runs this app to set it up.</span>
        </p>
      ) : (
        <form onSubmit={submit} className="auth-form">
          {mode === 'register' && (
            <label className="field">
              <span>Name</span>
              <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoComplete="name" maxLength={100} />
            </label>
          )}
          <label className="field">
            <span>Email</span>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete={mode === 'register' ? 'email' : 'username'}
              autoCapitalize="none"
              maxLength={254}
              required
            />
          </label>
          {(mode === 'signin' || mode === 'register') && (
            <PasswordField
              label="Password"
              value={password}
              onChange={setPassword}
              autoComplete={mode === 'signin' ? 'current-password' : 'new-password'}
              aside={
                mode === 'signin' && (
                  <button type="button" className="link-button small" onClick={() => go('forgot')}>
                    Forgot password?
                  </button>
                )
              }
            />
          )}
          {mode === 'register' && (
            <>
              <PasswordChecklist password={password} email={email} />
              <PasswordField label="Confirm password" value={confirm} onChange={setConfirm} autoComplete="new-password" invalid={!!mismatch} />
              {mismatch && <p className="error-text">{mismatch}</p>}
            </>
          )}
          {error && (
            <p className="error-text" role="alert">
              {error}
            </p>
          )}
          <button className="btn btn-primary btn-block" disabled={busy}>
            {busy
              ? 'Please wait…'
              : { signin: 'Sign in', register: 'Create account', forgot: 'Send reset link', link: 'Email me a link' }[mode]}
          </button>
          {mode === 'signin' && emailEnabled && (
            <button type="button" className="btn btn-block" onClick={() => go('link')}>
              <Icon name="mail" size={16} /> Email me a sign-in link
            </button>
          )}
        </form>
      )}
      <p className="auth-switch">
        {mode === 'signin' ? (
          <>
            New here?{' '}
            <button type="button" className="link-button" onClick={() => go('register')}>
              Create an account
            </button>
          </>
        ) : mode === 'register' ? (
          <>
            Already have an account?{' '}
            <button type="button" className="link-button" onClick={() => go('signin')}>
              Sign in
            </button>
          </>
        ) : (
          <button type="button" className="link-button" onClick={() => go('signin')}>
            Back to sign in
          </button>
        )}
      </p>
    </AuthCard>
  );
}
