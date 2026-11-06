import { describe, expect, it } from 'vitest';
import { parseAmounts, parseSigned } from './amounts';
import {
  checkAmount,
  checkCustomValue,
  checkDate,
  checkImportFile,
  checkMonth,
  checkName,
  checkOptions,
  checkSigned,
  checkText,
  cleanText,
  firstError,
  isRealDate,
} from './validate';

const error = (r: { ok: boolean; error?: string }) => (r.ok ? null : r.error);

describe('parseAmounts messages', () => {
  it('explains what went wrong', () => {
    expect(error(parseAmounts('abc'))).toBe('Enter an amount like 1,250 or 100+50');
    expect(error(parseAmounts('-50'))).toBe("Amounts can't be negative");
    expect(error(parseAmounts('12.345'))).toBe('Amounts can have at most 2 decimals');
    expect(error(parseAmounts('1000000000'))).toBe("Amounts can't be more than 999,999,999.99");
    expect(error(parseAmounts('100+'))).toBe('Missing a number after “+”');
  });

  it('accepts everything the server accepts', () => {
    expect(parseAmounts('999,999,999.99')).toEqual({ ok: true, amounts: [999_999_999.99] });
    expect(parseAmounts('.5')).toEqual({ ok: true, amounts: [0.5] });
    expect(parseAmounts('5.')).toEqual({ ok: true, amounts: [5] });
    expect(parseAmounts('1,25,000')).toEqual({ ok: true, amounts: [125_000] });
  });

  it('caps signed amounts too', () => {
    expect(parseSigned('-1000000000').ok).toBe(false);
    expect(error(parseSigned('1.234'))).toBe('Amounts can have at most 2 decimals');
  });
});

describe('checkAmount', () => {
  it('needs one positive amount', () => {
    expect(checkAmount('1,250')).toEqual({ ok: true, value: 1250 });
    expect(error(checkAmount(''))).toBe('Enter an amount');
    expect(error(checkAmount('0'))).toBe('Amount must be greater than zero');
    expect(error(checkAmount('100+50'))).toBe('Enter one amount');
  });

  it('adds up terms when asked and can allow zero or nothing', () => {
    expect(checkAmount('100+50.25', { sum: true })).toEqual({ ok: true, value: 150.25 });
    expect(checkAmount('0', { allowZero: true })).toEqual({ ok: true, value: 0 });
    expect(checkAmount('  ', { required: false })).toEqual({ ok: true, value: null });
    expect(error(checkAmount('999999999.99+1', { sum: true }))).toBe("Amounts can't be more than 999,999,999.99");
  });

  it('reads signed balances', () => {
    expect(checkSigned('-2,000')).toEqual({ ok: true, value: -2000 });
    expect(checkSigned('')).toEqual({ ok: true, value: null });
    expect(checkSigned('lots').ok).toBe(false);
  });
});

describe('dates and months', () => {
  it('knows real calendar dates', () => {
    expect(isRealDate('2028-02-29')).toBe(true);
    expect(isRealDate('2026-02-29')).toBe(false);
    expect(isRealDate('2026-13-01')).toBe(false);
    expect(isRealDate('26-11-05')).toBe(false);
  });

  it('keeps dates in range and within the given bounds', () => {
    expect(checkDate('2026-11-05')).toEqual({ ok: true, value: '2026-11-05' });
    expect(error(checkDate(''))).toBe('Date is required');
    expect(error(checkDate('1999-12-31'))).toBe('Date must be between 2000 and 2100');
    expect(error(checkDate('20266-11-05'))).toBe('Date must be a date like 2026-11-05');
    expect(error(checkDate('2026-11-20', { max: '2026-11-15', maxMessage: "Paid entries can't be in the future" }))).toBe(
      "Paid entries can't be in the future",
    );
    expect(error(checkDate('2026-11-01', { label: 'Refund date', min: '2026-11-04' }))).toBe("Refund date can't be before 2026-11-04");
  });

  it('checks months', () => {
    expect(checkMonth('2026-11')).toEqual({ ok: true, value: '2026-11' });
    expect(error(checkMonth('2026-13'))).toBe('Month must look like 2026-11');
    expect(error(checkMonth('2101-01', 'Start'))).toBe('Start must be between 2000 and 2100');
    expect(error(checkMonth(''))).toBe('Month is required');
  });
});

describe('text', () => {
  it('cleans control characters and trims like the server', () => {
    expect(cleanText('  Lunch\u0000 at\tthe\ncafe  ')).toBe('Lunch  at the cafe');
  });

  it('limits length and can require a value', () => {
    expect(checkText('x'.repeat(500), 500, 'Description').ok).toBe(true);
    expect(error(checkText('x'.repeat(501), 500, 'Description'))).toBe('Description can be at most 500 characters');
    expect(error(checkText('  ', 200, 'Name', { required: true }))).toBe('Name is required');
  });

  it('keeps names unique ignoring case', () => {
    expect(checkName(' Pets ', 100, ['Food'], 'category')).toEqual({ ok: true, value: 'Pets' });
    expect(error(checkName('food', 100, ['Food'], 'category'))).toBe('There\'s already a category called “food”');
    expect(error(checkName('  ', 100, [], 'column'))).toBe('Give the column a name');
  });

  it('checks dropdown options', () => {
    expect(checkOptions(['Daraz', ' ', 'Imtiaz '])).toEqual({ ok: true, value: ['Daraz', 'Imtiaz'] });
    expect(error(checkOptions(['', ' ']))).toBe('A dropdown needs at least one option');
    expect(error(checkOptions(['Daraz', 'daraz']))).toBe('“daraz” is listed twice; each option must be different');
    expect(checkOptions(['x'.repeat(61)]).ok).toBe(false);
  });

  it('reports the first problem', () => {
    expect(firstError(null, checkAmount('5'), checkDate(''), 'later')).toBe('Date is required');
    expect(firstError(checkAmount('5'), false)).toBeNull();
  });
});

describe('checkCustomValue', () => {
  const column = (type: 'text' | 'money' | 'number' | 'date' | 'dropdown' | 'checkbox', options: string[] = []) => ({ name: 'Tip', type, options });

  it('treats blanks as clearing the cell', () => {
    expect(checkCustomValue(column('money'), '  ')).toEqual({ ok: true, value: null });
    expect(checkCustomValue(column('text'), '')).toEqual({ ok: true, value: null });
  });

  it('checks numbers and money', () => {
    expect(checkCustomValue(column('number'), '1,200')).toEqual({ ok: true, value: 1200 });
    expect(error(checkCustomValue(column('number'), 'lots'))).toBe('“Tip” needs a number');
    expect(error(checkCustomValue(column('money'), '1.234'))).toBe('“Tip” allows at most 2 decimals');
    expect(error(checkCustomValue(column('money'), '5000000000'))).toBe('“Tip” is too large');
    expect(checkCustomValue(column('number'), '-3.125')).toEqual({ ok: true, value: -3.125 });
  });

  it('checks dates, checkboxes, dropdowns and text', () => {
    expect(checkCustomValue(column('date'), '1990-05-01')).toEqual({ ok: true, value: '1990-05-01' });
    expect(error(checkCustomValue(column('date'), '2026-02-30'))).toBe('“Tip” needs a date like 2026-11-05');
    expect(error(checkCustomValue(column('date'), '1850-01-01'))).toBe('“Tip” must be between 1900 and 2100');
    expect(checkCustomValue(column('checkbox'), true)).toEqual({ ok: true, value: true });
    expect(checkCustomValue(column('checkbox'), 'yes').ok).toBe(false);
    expect(checkCustomValue(column('dropdown', ['Daraz']), 'daraz')).toEqual({ ok: true, value: 'Daraz' });
    expect(error(checkCustomValue(column('dropdown', ['Daraz']), 'Hyperstar'))).toBe('“Hyperstar” isn\'t one of the options for “Tip”');
    expect(error(checkCustomValue(column('text'), 'x'.repeat(501)))).toBe('“Tip” is limited to 500 characters');
  });
});

describe('checkImportFile', () => {
  it('takes a CSV under 5 MB', () => {
    expect(checkImportFile({ name: 'Daily Log.csv', size: 2048, type: '' }).ok).toBe(true);
    expect(checkImportFile({ name: 'export', size: 2048, type: 'text/csv' }).ok).toBe(true);
    expect(checkImportFile({ name: 'Budget.xlsx', size: 2048, type: 'application/vnd.ms-excel' }).ok).toBe(false);
    expect(error(checkImportFile({ name: 'big.csv', size: 6 * 1024 * 1024, type: 'text/csv' }))).toBe('The file is too large; the limit is 5 MB');
    expect(error(checkImportFile({ name: 'empty.csv', size: 0, type: 'text/csv' }))).toBe('That file is empty');
    expect(error(checkImportFile(null))).toBe('Choose a .csv file');
  });
});
