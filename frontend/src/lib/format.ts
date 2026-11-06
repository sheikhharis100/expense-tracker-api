const whole = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const precise = new Intl.NumberFormat('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 2 });
const compact = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });

// Like the sheet: "14,920", and a dash for nothing.
export function money(value: number | null | undefined, { dash = true } = {}): string {
  if (value == null || value === 0) return dash ? '–' : '0';
  return Number.isInteger(value) ? whole.format(value) : precise.format(value);
}

// Negative amounts with a real minus sign.
export function signed(value: number, { dash = true } = {}): string {
  if (value === 0) return dash ? '–' : '0';
  return value < 0 ? `−${money(-value)}` : money(value);
}

export function signedChange(value: number): string {
  if (value === 0) return 'no change';
  return (value > 0 ? '+' : '−') + money(Math.abs(value));
}

export function compactMoney(value: number): string {
  return Math.abs(value) >= 10_000 ? compact.format(value) : whole.format(value);
}

export function percent(value: number | null | undefined): string {
  return value == null ? 'N/A' : `${value.toFixed(1)}%`;
}

export const orNA = (value: number | null | undefined, fmt: (v: number) => string = (v) => money(v, { dash: false })) =>
  value == null ? 'N/A' : fmt(value);

export const notSet = (value: number | null | undefined) => (value == null ? 'Not set' : signed(value, { dash: false }));
