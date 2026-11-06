// A copy of the server's password rules (backend/Services/PasswordPolicy.cs) for the live checklist and strength
// meter. The server has the full common-password list and the breach check, so it can still refuse a password that
// passes here; its message is shown then.

export const MIN_LENGTH = 8;
export const MAX_LENGTH = 128;

export const MESSAGES = {
  tooShort: 'Use at least 8 characters.',
  tooLong: 'Use 128 characters or fewer.',
  spaces: "Spaces aren't allowed in passwords.",
  common: 'This password is too common — pick something less guessable.',
  emailName: "Don't use your email name in your password.",
} as const;

// The most guessed passwords, and the ones people here reach for first.
const COMMON = new Set([
  '123456', 'password', '12345678', 'qwerty', '123456789', '12345', '1234', '111111', '1234567', 'dragon', '123123', 'baseball',
  'abc123', 'football', 'monkey', 'letmein', 'shadow', 'master', '666666', 'qwertyuiop', '123321', '1234567890', 'superman',
  '1qaz2wsx', '7777777', '121212', '000000', 'qazwsx', '123qwe', 'trustno1', 'zxcvbnm', 'asdfgh', 'sunshine', 'iloveyou',
  'princess', 'starwars', 'welcome', 'freedom', 'whatever', 'secret', 'love', 'hello', 'admin', 'login', 'changeme',
  '11111111', '00000000', '12341234', '11223344', '88888888', 'qwertyui', 'asdfghjk', 'abcd1234', 'qwer1234', 'q1w2e3r4',
  '1q2w3e4r', 'password1', 'password123', 'passw0rd', 'p@ssw0rd', 'qwerty123', 'iloveyou1', 'welcome1', 'test1234',
  'pakistan', 'pakistani', 'pakistan123', 'karachi', 'lahore', 'islamabad', 'peshawar', 'allah', 'allah786', '786786',
  'bismillah', 'muhammad', 'mohammad', 'ahmed', 'hassan', 'hussain', 'imran', 'cricket', 'zindabad',
]);

const ROWS = [
  'abcdefghijklmnopqrstuvwxyz', '01234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm', 'qwertyuiopasdfghjklzxcvbnm',
  '1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik9ol0p', 'qazwsxedcrfvtgbyhnujmikolp', '1q2w3e4r5t6y7u8i9o0p', '!@#$%^&*()',
];
const SEQUENCES = [...ROWS, ...ROWS.map((r) => [...r].reverse().join(''))];
const LEET: Record<string, string> = { '@': 'a', '4': 'a', '0': 'o', '3': 'e', '1': 'i', '!': 'i', $: 's', '5': 's', '7': 't' };

// Characters as people see them, so an emoji counts once.
export function visibleLength(s: string): number {
  if (typeof Intl !== 'undefined' && 'Segmenter' in Intl) return [...new Intl.Segmenter().segment(s)].length;
  return Array.from(s).length;
}

const unleet = (s: string) => [...s].map((c) => LEET[c] ?? c).join('');
const isLetter = (c: string) => /\p{L}/u.test(c);

function isPattern(s: string): boolean {
  if (new Set(s).size < 3) return true;
  for (let block = 1; block <= s.length / 2; block++) {
    let repeats = true;
    for (let i = block; i < s.length && repeats; i++) repeats = s[i] === s[i % block];
    if (repeats) return true;
  }
  return SEQUENCES.some((seq) => seq.includes(s));
}

function isGuessable(lower: string): boolean {
  if (lower.includes('expense') || isPattern(lower) || COMMON.has(lower) || COMMON.has(unleet(lower))) return true;
  // A common word with digits or symbols around it ("Pakistan@123"), unless the extra part is long and random.
  let start = 0;
  let end = lower.length;
  while (start < end && !isLetter(lower[start])) start++;
  while (end > start && !isLetter(lower[end - 1])) end--;
  const core = lower.slice(start, end);
  const rest = lower.slice(0, start) + lower.slice(end);
  if (core.length < 3 || rest.length === 0) return false;
  return (COMMON.has(core) || COMMON.has(unleet(core))) && (rest.length <= core.length || isPattern(rest));
}

const emailName = (email: string) => email.trim().toLowerCase().split('@')[0] ?? '';

// What's wrong with a new password, in the server's words, or null.
export function passwordProblem(password: string, email = ''): string | null {
  if (/\s/.test(password)) return MESSAGES.spaces;
  const length = visibleLength(password);
  if (length < MIN_LENGTH) return MESSAGES.tooShort;
  if (length > MAX_LENGTH) return MESSAGES.tooLong;
  const lower = password.toLowerCase();
  const name = emailName(email);
  if (name.length >= 4 && lower.includes(name)) return MESSAGES.emailName;
  return isGuessable(lower) ? MESSAGES.common : null;
}

export interface PasswordCheck {
  id: 'length' | 'spaces' | 'common';
  label: string;
  ok: boolean;
}

export function passwordChecks(password: string, email = ''): PasswordCheck[] {
  const length = visibleLength(password);
  const lower = password.toLowerCase();
  const name = emailName(email);
  return [
    { id: 'length', label: '8+ characters', ok: length >= MIN_LENGTH && length <= MAX_LENGTH },
    { id: 'spaces', label: 'No spaces', ok: password.length > 0 && !/\s/.test(password) },
    {
      id: 'common',
      label: 'Not a common password',
      ok: password.length > 0 && !isGuessable(lower) && !(name.length >= 4 && lower.includes(name)),
    },
  ];
}

export type StrengthScore = 0 | 1 | 2 | 3 | 4;
export const STRENGTH_LABELS = ['Too weak', 'Weak', 'Fair', 'Good', 'Strong'] as const;

// Length counts most (NIST); mixing letters, capitals, digits and symbols adds a step. Anything the rules refuse is 0.
export function passwordStrength(password: string, email = ''): { score: StrengthScore; label: string } {
  if (passwordProblem(password, email)) return { score: 0, label: STRENGTH_LABELS[0] };
  const length = visibleLength(password);
  const variety = [/[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z0-9]/].filter((r) => r.test(password)).length;
  const score = Math.min(4, (length >= 16 ? 3 : length >= 12 ? 2 : 1) + (variety >= 3 ? 1 : 0)) as StrengthScore;
  return { score, label: STRENGTH_LABELS[score] };
}
