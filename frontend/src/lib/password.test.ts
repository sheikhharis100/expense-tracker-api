import { describe, expect, it } from 'vitest';
import { MESSAGES, passwordChecks, passwordProblem, passwordStrength, visibleLength } from './password';

const okIds = (password: string, email = '') =>
  passwordChecks(password, email)
    .filter((c) => c.ok)
    .map((c) => c.id);

describe('passwordChecks', () => {
  it('ticks everything for a good password', () => {
    expect(okIds('Tr4ck-my-rupees')).toEqual(['length', 'spaces', 'common']);
  });

  it('ticks nothing while the field is empty', () => {
    expect(okIds('')).toEqual([]);
  });

  it('wants 8 characters, counting an emoji once', () => {
    expect(okIds('Kq7-xyz')).not.toContain('length');
    expect(okIds('Kq7-xyzw')).toContain('length');
    expect(visibleLength('🔑🏠🚗🍵🌙🌟🎉')).toBe(7);
  });

  it('refuses spaces of any kind', () => {
    expect(okIds('Tr4ck my rupees')).not.toContain('spaces');
    expect(okIds('Tr4ck-my-rupees\t')).not.toContain('spaces');
  });

  it.each(['password', '12345678', 'qwertyui', 'iloveyou', 'pakistan', '11111111', 'P@ssw0rd', 'Pakistan@123', 'love12345678', 'abcabcabc', '87654321', 'my-expense-log'])(
    'flags %s as common',
    (password) => {
      expect(okIds(password)).not.toContain('common');
    },
  );

  it('flags the email name inside the password', () => {
    expect(okIds('sana.malik-2026', 'Sana.Malik@example.com')).not.toContain('common');
    expect(okIds('sana.malik-2026', 'other@example.com')).toContain('common');
  });
});

describe('passwordProblem', () => {
  it('uses the same words as the server', () => {
    expect(passwordProblem('short')).toBe(MESSAGES.tooShort);
    expect(passwordProblem('two words here')).toBe(MESSAGES.spaces);
    expect(passwordProblem('password123')).toBe(MESSAGES.common);
    expect(passwordProblem('sana.malik-2026', 'sana.malik@example.com')).toBe(MESSAGES.emailName);
    expect(passwordProblem('x'.repeat(60) + '-Kq7' + 'y'.repeat(65))).toBe(MESSAGES.tooLong);
    expect(passwordProblem('imran90517386')).toBeNull();
    expect(passwordProblem('پاسورڈ-محفوظ-ہے')).toBeNull();
  });
});

describe('passwordStrength', () => {
  it('is too weak whenever a rule fails', () => {
    expect(passwordStrength('').label).toBe('Too weak');
    expect(passwordStrength('Pakistan@123').score).toBe(0);
  });

  it('grows with length, and with a mix of character types', () => {
    expect(passwordStrength('kitten77').label).toBe('Weak');
    expect(passwordStrength('Kitten-77').label).toBe('Fair');
    expect(passwordStrength('mangoes-in-june').label).toBe('Fair');
    expect(passwordStrength('Tr4ck-my-rupees').label).toBe('Good');
    expect(passwordStrength('correcthorsebatterystaple').label).toBe('Good');
    expect(passwordStrength('Kitten-Biryani-42').label).toBe('Strong');
  });
});
