import { useId, useState, type ReactNode } from 'react';
import { MAX_LENGTH, passwordChecks, passwordStrength } from '../lib/password';
import { Icon } from './Icon';

interface PasswordFieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: 'current-password' | 'new-password';
  // Shown next to the label (e.g. a "Forgot password?" link).
  aside?: ReactNode;
  invalid?: boolean;
}

// A password input with a show/hide button, so people can check what they typed on a phone keyboard.
export function PasswordField({ label, value, onChange, autoComplete, aside, invalid }: PasswordFieldProps) {
  const [shown, setShown] = useState(false);
  const id = useId();
  return (
    <div className="field pw-field">
      <div className="pw-label-row">
        <label htmlFor={id}>{label}</label>
        {aside}
      </div>
      <div className="pw-input">
        <input
          id={id}
          type={shown ? 'text' : 'password'}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          autoComplete={autoComplete}
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          maxLength={MAX_LENGTH * 2}
          aria-invalid={invalid || undefined}
          required
        />
        <button
          type="button"
          className="pw-toggle"
          onClick={() => setShown(!shown)}
          aria-label={shown ? 'Hide password' : 'Show password'}
          aria-pressed={shown}
        >
          <Icon name={shown ? 'eyeOff' : 'eye'} size={18} />
        </button>
      </div>
    </div>
  );
}

// The live checklist and strength meter under a new password. The server checks again (and against known breaches).
export function PasswordChecklist({ password, email }: { password: string; email?: string }) {
  const empty = password.length === 0;
  const checks = passwordChecks(password, email);
  const strength = passwordStrength(password, email);
  return (
    <div className="pw-help">
      <ul className="pw-checks" aria-label="Password rules">
        {checks.map((c) => (
          <li key={c.id} className={empty ? '' : c.ok ? 'pw-ok' : 'pw-bad'}>
            <Icon name={c.ok ? 'check' : empty ? 'lock' : 'x'} size={13} />
            {c.label}
            <span className="sr-only">{empty ? '' : c.ok ? ' (done)' : ' (not yet)'}</span>
          </li>
        ))}
      </ul>
      {!empty && (
        <div className="pw-meter" data-score={strength.score} aria-live="polite">
          <span className="pw-bars" aria-hidden="true">
            {[1, 2, 3, 4].map((i) => (
              <span key={i} className={i <= Math.max(strength.score, 1) ? 'on' : ''} />
            ))}
          </span>
          <span className="pw-strength">Strength: {strength.label}</span>
        </div>
      )}
    </div>
  );
}

// "Passwords don't match" as soon as the second one is long enough to tell.
export function confirmProblem(password: string, confirm: string): string | null {
  return confirm.length > 0 && confirm !== password && (confirm.length >= password.length || !password.startsWith(confirm))
    ? "Passwords don't match."
    : null;
}
