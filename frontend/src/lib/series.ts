// The fields of a category a chart series needs.
export interface CategoryRef {
  id: number;
  name: string;
  color: string;
}

export const OTHER_COLOR = '#898781';
// The categorical palette has 8 validated slots; past that, the smallest columns fold into "Other".
export const MAX_SERIES = 8;

export interface Series {
  key: string;
  name: string;
  color: string;
  ids: number[];
}

// Columns keep their own colour; a 9th+ column never gets a generated hue.
export function foldSeries(categories: CategoryRef[], totals: Record<string, number>, color: (hex: string) => string): Series[] {
  const withSpend = categories.filter((c) => (totals[c.id] ?? 0) > 0);
  if (withSpend.length <= MAX_SERIES) {
    return withSpend.map((c) => ({ key: String(c.id), name: c.name, color: color(c.color), ids: [c.id] }));
  }
  const ranked = [...withSpend].sort((a, b) => (totals[b.id] ?? 0) - (totals[a.id] ?? 0));
  const keep = new Set(ranked.slice(0, MAX_SERIES - 1).map((c) => c.id));
  const kept = withSpend.filter((c) => keep.has(c.id)).map((c) => ({ key: String(c.id), name: c.name, color: color(c.color), ids: [c.id] }));
  const rest = withSpend.filter((c) => !keep.has(c.id));
  return [...kept, { key: 'other', name: `Other (${rest.length})`, color: OTHER_COLOR, ids: rest.map((c) => c.id) }];
}
