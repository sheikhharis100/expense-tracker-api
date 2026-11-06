import { useSyncExternalStore } from 'react';

// "system" follows the phone/computer setting; light and dark override it. Kept per device, like the phone's own setting.
export type ThemeChoice = 'system' | 'light' | 'dark';
export type Theme = 'light' | 'dark';

// Also read by the inline script in index.html, which applies the theme before the first paint.
export const THEME_KEY = 'expense-tracker.theme';

// The page background of each theme, used for the phone's status bar.
const BAR_COLOR: Record<Theme, string> = { light: '#f3f7f6', dark: '#070b14' };

const hasWindow = typeof window !== 'undefined';
const systemDark = hasWindow && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
const listeners = new Set<() => void>();

function readChoice(): ThemeChoice {
  try {
    const saved = localStorage.getItem(THEME_KEY);
    return saved === 'light' || saved === 'dark' ? saved : 'system';
  } catch {
    return 'system';
  }
}

let choice: ThemeChoice = hasWindow ? readChoice() : 'system';

export function resolveTheme(c: ThemeChoice, prefersDark = systemDark?.matches ?? false): Theme {
  return c === 'system' ? (prefersDark ? 'dark' : 'light') : c;
}

function apply() {
  const theme = resolveTheme(choice);
  document.documentElement.dataset.theme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', BAR_COLOR[theme]);
  // iPhone home-screen apps read this at launch, so a change shows from the next launch there.
  document.querySelector('meta[name="apple-mobile-web-app-status-bar-style"]')?.setAttribute('content', theme === 'dark' ? 'black-translucent' : 'default');
  listeners.forEach((fn) => fn());
}

export function setThemeChoice(next: ThemeChoice) {
  choice = next;
  try {
    if (next === 'system') localStorage.removeItem(THEME_KEY);
    else localStorage.setItem(THEME_KEY, next);
  } catch {
    // Still applies for this visit.
  }
  apply();
}

if (hasWindow) {
  systemDark?.addEventListener('change', () => {
    if (choice === 'system') apply();
  });
  // Another open tab changed it.
  window.addEventListener('storage', (e) => {
    if (e.key === THEME_KEY) {
      choice = readChoice();
      apply();
    }
  });
}

function subscribe(fn: () => void) {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

export function useTheme() {
  const current = useSyncExternalStore(subscribe, () => choice, () => 'system' as ThemeChoice);
  const theme = useSyncExternalStore(subscribe, () => resolveTheme(choice), () => 'light' as Theme);
  return { choice: current, theme, setChoice: setThemeChoice };
}
