import { useState, type FormEvent } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, currentUserId, setToken } from '../api/client';
import { useAuthOptions, useMe, useProfileStats } from '../api/hooks';
import type { AuthResponse, Me } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Avatar } from '../components/Avatar';
import { Icon } from '../components/Icon';
import { confirmProblem, PasswordChecklist, PasswordField } from '../components/PasswordField';
import { toast } from '../components/Toast';
import { parseIsoDay } from '../lib/dates';
import { passwordProblem } from '../lib/password';

const shortDate = (iso: string) => parseIsoDay(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
const count = new Intl.NumberFormat('en-US');

// Busy flag and inline error for a form that calls the API.
function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await work();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong');
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, setError, run };
}

function FormError({ message }: { message: string | null }) {
  return message ? (
    <p className="error-text" role="alert">
      {message}
    </p>
  ) : null;
}

export function ProfilePage() {
  const me = useMe();
  if (!me.data) return <p className="loading">Loading your profile…</p>;
  return (
    <div className="page">
      <header className="page-header">
        <div>
          <p className="eyebrow">Profile</p>
          <h1>Your account</h1>
        </div>
      </header>
      <ProfileSummary me={me.data} />
      <div className="grid-2">
        <div className="stack">
          <NameCard me={me.data} />
          <EmailCard me={me.data} />
        </div>
        <div className="stack">
          <PasswordCard me={me.data} />
          <DevicesCard />
        </div>
      </div>
      <DeleteCard />
    </div>
  );
}

function ProfileSummary({ me }: { me: Me }) {
  const stats = useProfileStats();
  const options = useAuthOptions();
  const resend = useAction();

  const sendLink = () =>
    resend.run(async () => {
      const res = await api<{ message: string }>('/api/me/verify-email/resend', { method: 'POST' });
      toast(res.message, 'success');
    });

  return (
    <section className="card profile-summary">
      <Avatar name={me.displayName} email={me.email} large />
      <div className="profile-who">
        <h2 className="profile-name">{me.displayName || 'Add your name below'}</h2>
        <p className="profile-email">
          <span className="profile-address">{me.email}</span>
          {me.emailVerified ? (
            <span className="badge badge-good">
              <Icon name="check" size={11} /> Verified
            </span>
          ) : (
            me.emailVerified === false && <span className="badge badge-warn">Not verified</span>
          )}
        </p>
        {me.emailVerified === false && options.data?.emailEnabled && (
          <p className="small muted">
            Confirm your email so you can reset your password if you ever forget it.{' '}
            <button type="button" className="link-button small" onClick={sendLink} disabled={resend.busy}>
              {resend.busy ? 'Sending…' : 'Resend link'}
            </button>
          </p>
        )}
        <FormError message={resend.error} />
      </div>
      <dl className="profile-stats">
        <div>
          <dt>Member since</dt>
          <dd>{me.memberSince ? shortDate(me.memberSince) : '–'}</dd>
        </div>
        <div>
          <dt>Entries logged</dt>
          <dd>{stats.data ? count.format(stats.data.entriesLogged) : '…'}</dd>
        </div>
        <div>
          <dt>Months tracked</dt>
          <dd>{stats.data ? stats.data.monthsTracked : '…'}</dd>
        </div>
      </dl>
    </section>
  );
}

function NameCard({ me }: { me: Me }) {
  const qc = useQueryClient();
  const [name, setName] = useState(me.displayName);
  const action = useAction();
  const dirty = name.trim() !== me.displayName;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      await api('/api/me/profile', { method: 'PUT', body: { displayName: name } });
      setName(name.trim());
      await qc.invalidateQueries();
      toast('Name saved', 'success');
    });
  };

  return (
    <section className="card">
      <div className="card-head">
        <h2>Name</h2>
      </div>
      <form className="form-stack" onSubmit={submit}>
        <label className="field">
          <span>Shown in the app and in emails from it</span>
          <input value={name} maxLength={100} autoComplete="name" onChange={(e) => setName(e.target.value)} />
        </label>
        <FormError message={action.error} />
        <div className="form-actions">
          <button className="btn btn-primary" disabled={!dirty || action.busy}>
            Save name
          </button>
        </div>
      </form>
    </section>
  );
}

function EmailCard({ me }: { me: Me }) {
  const qc = useQueryClient();
  const options = useAuthOptions();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const action = useAction();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      const res = await api<AuthResponse>('/api/me/change-email', { method: 'POST', body: { newEmail: email, currentPassword: password } });
      setToken(res.token);
      setEmail('');
      setPassword('');
      await qc.invalidateQueries();
      toast(options.data?.emailEnabled ? `Email changed. Open the link we sent to ${res.email} to confirm it.` : 'Email changed.', 'success');
    });
  };

  return (
    <section className="card">
      <div className="card-head">
        <h2>Email</h2>
      </div>
      <p className="muted">
        You sign in with <b>{me.email}</b>. Reset links and sign-in links go to this address.
      </p>
      <form className="form-stack" onSubmit={submit}>
        <label className="field">
          <span>New email</span>
          <input type="email" value={email} maxLength={254} autoComplete="email" autoCapitalize="none" onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <PasswordField label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
        <FormError message={action.error} />
        <div className="form-actions">
          <button className="btn btn-primary" disabled={action.busy || !email.trim() || !password}>
            Change email
          </button>
        </div>
      </form>
    </section>
  );
}

function PasswordCard({ me }: { me: Me }) {
  const options = useAuthOptions();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const action = useAction();
  const mismatch = confirmProblem(next, confirm);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const problem =
      passwordProblem(next, me.email) ??
      (confirm !== next ? "Passwords don't match." : null) ??
      (next === current ? "Choose a password that's different from your current one." : null);
    if (problem) return action.setError(problem);
    void action.run(async () => {
      const res = await api<AuthResponse>('/api/me/change-password', { method: 'POST', body: { currentPassword: current, newPassword: next } });
      // The old token stopped working on every device, this one included; carry on with the new one.
      setToken(res.token);
      setCurrent('');
      setNext('');
      setConfirm('');
      toast('Password changed. Your other devices were signed out.', 'success');
    });
  };

  // For someone signed in by email link who never knew (or forgot) the current password.
  const sendReset = () =>
    action.run(async () => {
      await api('/api/auth/forgot-password', { method: 'POST', body: { email: me.email }, auth: false });
      toast(`A reset link is on its way to ${me.email}. Check spam too.`, 'success');
    });

  return (
    <section className="card">
      <div className="card-head">
        <h2>Password</h2>
      </div>
      <form className="form-stack" onSubmit={submit}>
        <PasswordField
          label="Current password"
          value={current}
          onChange={setCurrent}
          autoComplete="current-password"
          aside={
            options.data?.emailEnabled && (
              <button type="button" className="link-button small" onClick={sendReset} disabled={action.busy}>
                Forgot it?
              </button>
            )
          }
        />
        <PasswordField label="New password" value={next} onChange={setNext} autoComplete="new-password" />
        <PasswordChecklist password={next} email={me.email} />
        <PasswordField label="Confirm new password" value={confirm} onChange={setConfirm} autoComplete="new-password" invalid={!!mismatch} />
        {mismatch && <p className="error-text">{mismatch}</p>}
        <FormError message={action.error} />
        <p className="muted small">Changing it signs you out on your other devices.</p>
        <div className="form-actions">
          <button className="btn btn-primary" disabled={action.busy || !current || !next || !confirm}>
            Change password
          </button>
        </div>
      </form>
    </section>
  );
}

function DevicesCard() {
  const { signOut } = useAuth();
  const action = useAction();

  const everywhere = () => {
    if (!confirm('Sign out on every other phone and computer? This device stays signed in.')) return;
    void action.run(async () => {
      const res = await api<AuthResponse>('/api/me/sign-out-everywhere', { method: 'POST' });
      setToken(res.token);
      toast('Signed out on all your other devices.', 'success');
    });
  };

  return (
    <section className="card">
      <div className="card-head">
        <h2>Devices</h2>
      </div>
      <p className="muted">Lost a phone, or signed in on a shared computer? Sign out everywhere else. This device stays signed in.</p>
      <div className="btn-row">
        <button type="button" className="btn" onClick={everywhere} disabled={action.busy}>
          <Icon name="shield" size={16} /> Sign out of all other devices
        </button>
        <button type="button" className="btn" onClick={signOut}>
          <Icon name="logout" size={16} /> Sign out
        </button>
      </div>
      <FormError message={action.error} />
    </section>
  );
}

// Forgets what this device kept for the account (cached profile, entries waiting to sync).
function forgetDevice(userId: string | null) {
  if (!userId) return;
  try {
    for (const key of Object.keys(localStorage))
      if (key.startsWith(`expense-tracker.cache.${userId}.`) || key === `expense-tracker.offline-queue.${userId}`) localStorage.removeItem(key);
  } catch {
    // Storage blocked: nothing was kept.
  }
}

function DeleteCard() {
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [understood, setUnderstood] = useState(false);
  const action = useAction();

  const cancel = () => {
    setOpen(false);
    setPassword('');
    setUnderstood(false);
    action.setError(null);
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      const userId = currentUserId();
      await api('/api/me', { method: 'DELETE', body: { currentPassword: password } });
      forgetDevice(userId);
      setToken(null);
      toast('Your account and all its data were deleted.', 'success');
    });
  };

  return (
    <section className="card danger-zone" id="delete">
      <div className="card-head">
        <h2>
          <Icon name="alert" size={18} /> Delete account
        </h2>
      </div>
      <p>
        This permanently deletes your account and everything in it: every entry, salary, budget, category and note, on all your devices.{' '}
        <b>It can't be undone.</b> Want a copy first? Download it from the Year page.
      </p>
      {!open ? (
        <button type="button" className="btn btn-danger-ghost" onClick={() => setOpen(true)}>
          <Icon name="trash" size={16} /> Delete my account…
        </button>
      ) : (
        <form className="form-stack" onSubmit={submit}>
          <PasswordField label="Your password" value={password} onChange={setPassword} autoComplete="current-password" />
          <label className="toggle">
            <input type="checkbox" checked={understood} onChange={(e) => setUnderstood(e.target.checked)} />
            <span>I understand everything will be deleted for good.</span>
          </label>
          <FormError message={action.error} />
          <div className="form-actions">
            <button className="btn btn-danger" disabled={!password || !understood || action.busy}>
              {action.busy ? 'Deleting…' : 'Delete everything'}
            </button>
            <button type="button" className="btn" onClick={cancel}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
