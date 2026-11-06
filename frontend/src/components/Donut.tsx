import { useState } from 'react';
import { money } from '../lib/format';

export interface Slice {
  key: string;
  label: string;
  value: number;
  color: string;
}

const SIZE = 196;
const R = 80;
const WIDTH = 24;
const GAP = 2; // px of surface between slices

function arc(start: number, end: number): string {
  const r = R;
  const c = SIZE / 2;
  const p = (a: number) => [c + r * Math.sin(a), c - r * Math.cos(a)];
  const [x1, y1] = p(start);
  const [x2, y2] = p(end);
  const large = end - start > Math.PI ? 1 : 0;
  return `M${x1},${y1}A${r},${r} 0 ${large} 1 ${x2},${y2}`;
}

// Share of the month's spending per column. Legend doubles as the table view and the keyboard path.
export function Donut({ slices, total, centerLabel }: { slices: Slice[]; total: number; centerLabel: string }) {
  const [active, setActive] = useState<string | null>(null);
  const gapAngle = GAP / R;
  let cursor = 0;

  const arcs = slices.map((s) => {
    const sweep = total > 0 ? (s.value / total) * Math.PI * 2 : 0;
    const start = cursor + (slices.length > 1 ? gapAngle / 2 : 0);
    const end = cursor + sweep - (slices.length > 1 ? gapAngle / 2 : 0);
    cursor += sweep;
    return { ...s, d: slices.length === 1 ? null : arc(start, Math.max(start, end)) };
  });

  const focus = slices.find((s) => s.key === active);

  return (
    <div className="donut">
      <svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`} role="img" aria-label={`${centerLabel}: ${money(total, { dash: false })}`}>
        <circle cx={SIZE / 2} cy={SIZE / 2} r={R} className="donut-track" strokeWidth={WIDTH} fill="none" />
        {arcs.map((a) =>
          a.d ? (
            <path
              key={a.key}
              d={a.d}
              stroke={a.color}
              strokeWidth={active === a.key ? WIDTH + 6 : WIDTH}
              fill="none"
              className={`donut-slice ${active && active !== a.key ? 'donut-dim' : ''}`}
              onPointerEnter={() => setActive(a.key)}
              onPointerLeave={() => setActive(null)}
            />
          ) : (
            <circle key={a.key} cx={SIZE / 2} cy={SIZE / 2} r={R} stroke={a.color} strokeWidth={WIDTH} fill="none" />
          ),
        )}
        <text x="50%" y="46%" textAnchor="middle" className="donut-value">
          {money(focus ? focus.value : total, { dash: false })}
        </text>
        <text x="50%" y="58%" textAnchor="middle" className="donut-label">
          {focus ? `${Math.round((focus.value / total) * 100)}% · ${focus.label}`.slice(0, 28) : centerLabel}
        </text>
      </svg>

      <ul className="legend-list">
        {slices.map((s) => (
          <li key={s.key}>
            <button
              type="button"
              className={`legend-item ${active === s.key ? 'on' : ''}`}
              onPointerEnter={() => setActive(s.key)}
              onPointerLeave={() => setActive(null)}
              onFocus={() => setActive(s.key)}
              onBlur={() => setActive(null)}
            >
              <span className="legend-key" style={{ background: s.color }} aria-hidden="true" />
              <span className="legend-name">{s.label}</span>
              <span className="legend-value">{money(s.value)}</span>
              <span className="legend-share">{total > 0 ? Math.round((s.value / total) * 100) : 0}%</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
