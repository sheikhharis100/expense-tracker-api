import { describe, expect, it } from 'vitest';
import { clamp, dropIndex, moveItem } from './reorder';

// Four rows, 40px tall, starting at y=100: middles at 120, 160, 200, 240.
const rows = [0, 1, 2, 3].map((index) => ({ index, top: 100 + index * 40, height: 40 }));

describe('moveItem', () => {
  it('moves an item down or up without touching the input', () => {
    const list = ['a', 'b', 'c', 'd'];
    expect(moveItem(list, 0, 2)).toEqual(['b', 'c', 'a', 'd']);
    expect(moveItem(list, 3, 1)).toEqual(['a', 'd', 'b', 'c']);
    expect(list).toEqual(['a', 'b', 'c', 'd']);
  });
});

describe('dropIndex', () => {
  it('stays put until the pointer passes a neighbour’s middle', () => {
    expect(dropIndex(1, 150, rows)).toBe(1);
    expect(dropIndex(1, 165, rows)).toBe(1);
  });

  it('moves down past every middle crossed', () => {
    expect(dropIndex(0, 165, rows)).toBe(1);
    expect(dropIndex(0, 245, rows)).toBe(3);
  });

  it('moves up past every middle crossed', () => {
    expect(dropIndex(3, 195, rows)).toBe(2);
    expect(dropIndex(3, 90, rows)).toBe(0);
  });

  it('skips rows that are not rendered', () => {
    const gappy = rows.filter((r) => r.index !== 2);
    expect(dropIndex(0, 245, gappy)).toBe(3);
  });
});

describe('clamp', () => {
  it('keeps column widths between the limits', () => {
    expect(clamp(20, 60, 600)).toBe(60);
    expect(clamp(240, 60, 600)).toBe(240);
    expect(clamp(900, 60, 600)).toBe(600);
  });
});
