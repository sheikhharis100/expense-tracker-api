import { describe, expect, it } from 'vitest';
import { parseAmounts, parseSigned, sameAmounts, toExpression } from './amounts';

describe('parseAmounts', () => {
  it('reads a single amount', () => {
    expect(parseAmounts('625')).toEqual({ ok: true, amounts: [625] });
  });

  it('splits sheet-style sums into separate entries', () => {
    expect(parseAmounts('=100+50+200')).toEqual({ ok: true, amounts: [100, 50, 200] });
    expect(parseAmounts(' 1810 + 1000 ')).toEqual({ ok: true, amounts: [1810, 1000] });
  });

  it('accepts thousands separators and decimals', () => {
    expect(parseAmounts('14,920')).toEqual({ ok: true, amounts: [14920] });
    expect(parseAmounts('99.5')).toEqual({ ok: true, amounts: [99.5] });
  });

  it('treats empty and dash as a cleared cell', () => {
    expect(parseAmounts('')).toEqual({ ok: true, amounts: [] });
    expect(parseAmounts('-')).toEqual({ ok: true, amounts: [] });
    expect(parseAmounts('=')).toEqual({ ok: true, amounts: [] });
  });

  it('drops zero terms', () => {
    expect(parseAmounts('=0+100')).toEqual({ ok: true, amounts: [100] });
  });

  it('rejects text, negatives and dangling plus signs', () => {
    expect(parseAmounts('abc').ok).toBe(false);
    expect(parseAmounts('-50').ok).toBe(false);
    expect(parseAmounts('100+').ok).toBe(false);
    expect(parseAmounts('100*2').ok).toBe(false);
  });
});

describe('toExpression', () => {
  it('round-trips through parseAmounts', () => {
    for (const amounts of [[], [625], [1810, 1000], [100, 50, 200]]) {
      const parsed = parseAmounts(toExpression(amounts));
      expect(parsed.ok && parsed.amounts).toEqual(amounts);
    }
  });

  it('shows a plain number for one entry and a formula for several', () => {
    expect(toExpression([625])).toBe('625');
    expect(toExpression([2000, 400])).toBe('=2000+400');
  });
});

describe('sameAmounts', () => {
  it('compares in order', () => {
    expect(sameAmounts([1, 2], [1, 2])).toBe(true);
    expect(sameAmounts([1, 2], [2, 1])).toBe(false);
  });
});

describe('parseSigned', () => {
  it('reads bonuses and deductions', () => {
    expect(parseSigned('5,000')).toEqual({ ok: true, value: 5000 });
    expect(parseSigned('-2000')).toEqual({ ok: true, value: -2000 });
    expect(parseSigned('=1000-250')).toEqual({ ok: true, value: 750 });
    expect(parseSigned('')).toEqual({ ok: true, value: 0 });
  });

  it('rejects text', () => {
    expect(parseSigned('bonus').ok).toBe(false);
  });
});
