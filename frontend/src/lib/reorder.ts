export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  const next = [...items];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
}

// Where a dragged row belongs: it takes the place of the furthest row whose middle the pointer has passed.
export function dropIndex(from: number, pointerY: number, rows: { index: number; top: number; height: number }[]): number {
  let to = from;
  for (const r of rows) {
    const middle = r.top + r.height / 2;
    if (r.index < from && pointerY < middle) to = Math.min(to, r.index);
    if (r.index > from && pointerY > middle) to = Math.max(to, r.index);
  }
  return to;
}

export const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));
