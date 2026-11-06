import { useTheme } from './theme';

// Category colours are stored as the light-mode step of the categorical palette (backend DefaultData.Palette).
// Dark mode uses the same hues stepped for the dark surface.
export const PALETTE: { light: string; dark: string; name: string }[] = [
  { light: '#2a78d6', dark: '#3987e5', name: 'Blue' },
  { light: '#eb6834', dark: '#d95926', name: 'Orange' },
  { light: '#1baf7a', dark: '#199e70', name: 'Aqua' },
  { light: '#eda100', dark: '#c98500', name: 'Yellow' },
  { light: '#e87ba4', dark: '#d55181', name: 'Magenta' },
  { light: '#008300', dark: '#008300', name: 'Green' },
  { light: '#4a3aa7', dark: '#9085e9', name: 'Violet' },
  { light: '#e34948', dark: '#e66767', name: 'Red' },
];

const darkByLight = new Map(PALETTE.map((p) => [p.light.toLowerCase(), p.dark]));

// The theme the app shows (the user's choice, or the device setting), not just the device setting.
export function useDarkMode(): boolean {
  return useTheme().theme === 'dark';
}

export function useSeriesColor() {
  const dark = useDarkMode();
  return (stored: string) => (dark ? (darkByLight.get(stored.toLowerCase()) ?? stored) : stored);
}
