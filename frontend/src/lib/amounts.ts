// Cells work like the sheet: "100", "=100+50" or "100 + 50 + 200". Each "+" term is stored as its own entry.

export type ParsedAmounts = { ok: true; amounts: number[] } | { ok: false; error: string };

// The largest single amount the server accepts (an entry, a payment, a budget).
export const MAX_AMOUNT = 999_999_999.99;
export const MAX_AMOUNT_TEXT = '999,999,999.99';
const MAX_TERMS = 50;

export function parseAmounts(input: string): ParsedAmounts {
  const text = input.trim().replace(/^=/, '').trim();
  if (text === '' || text === '-') return { ok: true, amounts: [] };

  const terms = text.split('+').map((t) => t.trim());
  if (terms.length > MAX_TERMS) return { ok: false, error: `Up to ${MAX_TERMS} amounts per cell` };

  const amounts: number[] = [];
  for (const term of terms) {
    const cleaned = term.replace(/,/g, '');
    if (term === '') return { ok: false, error: 'Missing a number after “+”' };
    if (/^-\s*[\d.]/.test(cleaned)) return { ok: false, error: "Amounts can't be negative" };
    if (/^\d*\.\d{3,}$/.test(cleaned)) return { ok: false, error: 'Amounts can have at most 2 decimals' };
    if (!/^(\d+(\.\d{0,2})?|\.\d{1,2})$/.test(cleaned)) return { ok: false, error: 'Enter an amount like 1,250 or 100+50' };
    const value = Number(cleaned);
    if (value <= 0) continue;
    if (value > MAX_AMOUNT) return { ok: false, error: `Amounts can't be more than ${MAX_AMOUNT_TEXT}` };
    amounts.push(value);
  }
  return { ok: true, amounts };
}

// What a cell shows while you edit it: "=1810+1000" for several entries, "625" for one.
export function toExpression(amounts: number[]): string {
  if (amounts.length === 0) return '';
  if (amounts.length === 1) return String(amounts[0]);
  return '=' + amounts.join('+');
}

export function sameAmounts(a: number[], b: number[]): boolean {
  return a.length === b.length && a.every((v, i) => v === b[i]);
}

// Signed variant for the salary adjust cell: "-2000", "+500", "=1000-250".
export function parseSigned(input: string): { ok: true; value: number } | { ok: false; error: string } {
  const text = input.trim().replace(/^=/, '').replace(/,/g, '').replace(/\s+/g, '');
  if (text === '' || text === '-') return { ok: true, value: 0 };
  if (!/^[+-]?\d+(\.\d{1,2})?([+-]\d+(\.\d{1,2})?)*$/.test(text)) {
    return { ok: false, error: /\.\d{3,}/.test(text) ? 'Amounts can have at most 2 decimals' : 'Use a number like 5000 or -2000' };
  }
  const value = (text.match(/[+-]?\d+(\.\d+)?/g) ?? []).reduce((sum, t) => sum + Number(t), 0);
  if (Math.abs(value) > MAX_AMOUNT) return { ok: false, error: `Amounts can't be more than ${MAX_AMOUNT_TEXT}` };
  return { ok: true, value: Math.round(value * 100) / 100 };
}
