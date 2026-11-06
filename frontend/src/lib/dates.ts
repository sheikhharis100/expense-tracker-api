import { useEffect, useState } from 'react';

export interface YM {
  year: number;
  month: number; // 1-12
}

const SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const LONG = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

const pad = (n: number) => String(n).padStart(2, '0');

export const ymKey = ({ year, month }: YM) => `${year}-${pad(month)}`;
export const ymIndex = ({ year, month }: YM) => year * 12 + month - 1;
export const ymFromIndex = (i: number): YM => ({ year: Math.floor(i / 12), month: (i % 12) + 1 });
export const addMonths = (ym: YM, n: number) => ymFromIndex(ymIndex(ym) + n);
export const shiftKey = (key: string, n: number) => ymKey(addMonths(parseYm(key)!, n));

export function parseYm(value: string | null | undefined): YM | null {
  const m = /^(\d{4})-(\d{2})$/.exec(value ?? '');
  if (!m) return null;
  const ym = { year: Number(m[1]), month: Number(m[2]) };
  return ym.month >= 1 && ym.month <= 12 ? ym : null;
}

export const monthShort = (month: number) => SHORT[month - 1];
export const monthLong = (month: number) => LONG[month - 1];
export const ymLabel = (key: string) => {
  const ym = parseYm(key);
  return ym ? `${monthShort(ym.month)} ${ym.year}` : key;
};
export const ymLongLabel = (key: string) => {
  const ym = parseYm(key);
  return ym ? `${monthLong(ym.month)} ${ym.year}` : key;
};

export function parseIsoDay(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function dayLabel(iso: string): { day: string; weekday: string; dayNumber: number } {
  const d = parseIsoDay(iso);
  return { day: `${pad(d.getDate())} ${SHORT[d.getMonth()]}`, weekday: WEEKDAYS[d.getDay()], dayNumber: d.getDate() };
}

export const monthOfIso = (iso: string) => iso.slice(0, 7);

export function lastDayOf(key: string): string {
  const ym = parseYm(key)!;
  return `${key}-${pad(new Date(ym.year, ym.month, 0).getDate())}`;
}

// Today in the user's zone (Asia/Karachi by default), not the device's: month boundaries follow the zone.
export function todayIn(timeZone: string): string {
  try {
    return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  } catch {
    const d = new Date();
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }
}

// Re-checks every minute and when the app becomes visible, so the current month switches by itself.
export function useToday(timeZone: string): string {
  const [today, setToday] = useState(() => todayIn(timeZone));
  useEffect(() => {
    const check = () => setToday(todayIn(timeZone));
    check();
    const timer = window.setInterval(check, 60_000);
    document.addEventListener('visibilitychange', check);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', check);
    };
  }, [timeZone]);
  return today;
}
