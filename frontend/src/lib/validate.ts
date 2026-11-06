// The same limits the server checks, so a mistake shows next to the field while typing instead of after saving.
// Each check returns the cleaned value, or a short message. They never refuse input the server would accept.
import type { Column } from '../api/types';
import { MAX_AMOUNT, MAX_AMOUNT_TEXT, parseAmounts, parseSigned } from './amounts';

export type Check<T> = { ok: true; value: T } | { ok: false; error: string };

export const MIN_YEAR = 2000;
export const MAX_YEAR = 2100;
// For <input type="date" min max>, so the picker can't wander off either.
export const MIN_DATE = `${MIN_YEAR}-01-01`;
export const MAX_DATE = `${MAX_YEAR}-12-31`;
export const MIN_MONTH = `${MIN_YEAR}-01`;
export const MAX_MONTH = `${MAX_YEAR}-12`;

// Text lengths, matching the server.
export const LIMITS = {
  description: 500,
  note: 1000,
  name: 100,
  columnName: 60,
  option: 60,
  recurringName: 200,
  planNote: 200,
  label: 100,
} as const;

export const MAX_IMPORT_BYTES = 5 * 1024 * 1024;

// Plain numbers in custom columns (receipt no., quantity) aren't money but stay below what JS holds exactly.
const MAX_NUMBER = 999_999_999_999_999;
const MIN_CUSTOM_YEAR = 1900;

const ok = <T,>(value: T): Check<T> => ({ ok: true, value });
const fail = <T,>(error: string): Check<T> => ({ ok: false, error });

type Outcome = { ok: true } | { ok: false; error: string };

// The first problem among several checks (or plain messages), or null.
export const firstError = (...checks: (Outcome | string | null | undefined | false)[]): string | null => {
  for (const c of checks) {
    if (typeof c === 'string') return c;
    if (c && !c.ok) return c.error;
  }
  return null;
};

// One amount typed in a form. `sum` adds "100+50" up (budgets); otherwise several amounts need the sheet or Quick add.
export function checkAmount(text: string, { allowZero = false, sum = false, required = true } = {}): Check<number | null> {
  if (text.trim() === '') return required ? fail('Enter an amount') : ok(null);
  const parsed = parseAmounts(text);
  if (!parsed.ok) return parsed;
  if (parsed.amounts.length > 1 && !sum) return fail('Enter one amount');
  const value = Math.round(parsed.amounts.reduce((a, b) => a + b, 0) * 100) / 100;
  if (value === 0 && !allowZero) return fail('Amount must be greater than zero');
  if (value > MAX_AMOUNT) return fail(`Amounts can't be more than ${MAX_AMOUNT_TEXT}`);
  return ok(value);
}

// Opening balances and adjustments: either sign. Empty is null.
export function checkSigned(text: string): Check<number | null> {
  if (text.trim() === '') return ok(null);
  const parsed = parseSigned(text);
  return parsed.ok ? ok(parsed.value) : parsed;
}

export function isRealDate(iso: string): boolean {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!m) return false;
  const [y, mo, d] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const date = new Date(Date.UTC(y, mo - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === mo - 1 && date.getUTCDate() === d;
}

interface DateRules {
  label?: string;
  // ISO bounds with their own messages, e.g. { max: today, maxMessage: "Received money can't be in the future" }.
  min?: string;
  minMessage?: string;
  max?: string;
  maxMessage?: string;
}

export function checkDate(iso: string, { label = 'Date', min, minMessage, max, maxMessage }: DateRules = {}): Check<string> {
  if (!iso) return fail(`${label} is required`);
  if (!isRealDate(iso)) return fail(`${label} must be a date like 2026-11-05`);
  const year = Number(iso.slice(0, 4));
  if (year < MIN_YEAR || year > MAX_YEAR) return fail(`${label} must be between ${MIN_YEAR} and ${MAX_YEAR}`);
  if (min && iso < min) return fail(minMessage ?? `${label} can't be before ${min}`);
  if (max && iso > max) return fail(maxMessage ?? `${label} can't be after ${max}`);
  return ok(iso);
}

export function checkMonth(ym: string, label = 'Month'): Check<string> {
  if (!ym) return fail(`${label} is required`);
  const m = /^(\d{4})-(\d{2})$/.exec(ym);
  if (!m || Number(m[2]) < 1 || Number(m[2]) > 12) return fail(`${label} must look like 2026-11`);
  const year = Number(m[1]);
  if (year < MIN_YEAR || year > MAX_YEAR) return fail(`${label} must be between ${MIN_YEAR} and ${MAX_YEAR}`);
  return ok(ym);
}

// Like the server: control characters become spaces, then it's trimmed.
export const cleanText = (text: string) => text.replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ').trim();

export function checkText(text: string, max: number, label: string, { required = false } = {}): Check<string> {
  const value = cleanText(text);
  if (required && value === '') return fail(`${label} is required`);
  if (value.length > max) return fail(`${label} can be at most ${max} characters`);
  return ok(value);
}

// A required name that no other item of the same kind uses (ignoring case).
export function checkName(text: string, max: number, taken: string[], noun: string): Check<string> {
  const value = cleanText(text);
  if (value === '') return fail(`Give the ${noun} a name`);
  if (value.length > max) return fail(`Names can be at most ${max} characters`);
  const lower = value.toLowerCase();
  if (taken.some((t) => t.toLowerCase() === lower)) return fail(`There's already a ${noun} called “${value}”`);
  return ok(value);
}

// Dropdown options: blanks ignored, each short and different (ignoring case).
export function checkOptions(options: string[]): Check<string[]> {
  const list = options.map(cleanText).filter((o) => o !== '');
  if (list.length === 0) return fail('A dropdown needs at least one option');
  if (list.length > 100) return fail('A dropdown can have at most 100 options');
  const long = list.find((o) => o.length > LIMITS.option);
  if (long) return fail(`Options can be at most ${LIMITS.option} characters (“${long.slice(0, 20)}…”)`);
  const seen = new Set<string>();
  for (const o of list) {
    if (seen.has(o.toLowerCase())) return fail(`“${o}” is listed twice; each option must be different`);
    seen.add(o.toLowerCase());
  }
  return ok(list);
}

// A value for a custom column, as typed. Returns what to send: null clears the cell.
export function checkCustomValue(column: Pick<Column, 'name' | 'type' | 'options'>, raw: unknown): Check<unknown> {
  if (raw == null || (typeof raw === 'string' && raw.trim() === '' && column.type !== 'text')) return ok(null);
  const name = `“${column.name}”`;
  switch (column.type) {
    case 'text': {
      const text = cleanText(String(raw));
      if (text.length > LIMITS.description) return fail(`${name} is limited to ${LIMITS.description} characters`);
      return ok(text === '' ? null : text);
    }
    case 'money':
    case 'number': {
      const text = typeof raw === 'number' ? String(raw) : String(raw).replace(/,/g, '').trim();
      if (!/^[+-]?(\d+(\.\d*)?|\.\d+)$/.test(text)) return fail(`${name} needs a number`);
      const value = Number(text);
      if (column.type === 'money' && /\.\d{3,}$/.test(text)) return fail(`${name} allows at most 2 decimals`);
      if (Math.abs(value) > (column.type === 'money' ? MAX_AMOUNT : MAX_NUMBER)) return fail(`${name} is too large`);
      return ok(value);
    }
    case 'date': {
      const text = String(raw).trim();
      if (!isRealDate(text)) return fail(`${name} needs a date like 2026-11-05`);
      const year = Number(text.slice(0, 4));
      if (year < MIN_CUSTOM_YEAR || year > MAX_YEAR) return fail(`${name} must be between ${MIN_CUSTOM_YEAR} and ${MAX_YEAR}`);
      return ok(text);
    }
    case 'checkbox':
      return typeof raw === 'boolean' ? ok(raw) : fail(`${name} is a checkbox (true/false)`);
    case 'dropdown': {
      const text = String(raw).trim();
      const option = column.options.find((o) => o.toLowerCase() === text.toLowerCase());
      return option ? ok(option) : fail(`“${text}” isn't one of the options for ${name}`);
    }
    default:
      return fail(`${name} is calculated and can't be typed into`);
  }
}

// Imports: a .csv export under 5 MB.
export function checkImportFile(file: Pick<File, 'name' | 'size' | 'type'> | null): Check<true> {
  if (!file) return fail('Choose a .csv file');
  if (!/\.csv$/i.test(file.name) && !file.type.startsWith('text/csv')) return fail('Choose a .csv file (File → Download → Comma-separated values)');
  if (file.size === 0) return fail('That file is empty');
  if (file.size > MAX_IMPORT_BYTES) return fail('The file is too large; the limit is 5 MB');
  return ok(true);
}
