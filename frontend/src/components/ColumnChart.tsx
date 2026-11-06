import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { compactMoney, money } from '../lib/format';

export interface Segment {
  key: string;
  value: number;
  color: string;
  label: string;
}

export interface Column {
  key: string;
  // Axis label; empty strings are skipped so the axis stays sparse.
  tick: string;
  value: number;
  title: string;
  // Stacked columns: bottom-up segments that add up to value.
  segments?: Segment[];
  rows?: { label: string; value: string; color?: string }[];
  muted?: boolean;
  emphasis?: boolean;
}

interface Props {
  columns: Column[];
  label: string;
  height?: number;
  reference?: { value: number; label: string };
}

const PAD = { top: 12, right: 8, bottom: 26, left: 44 };
const SEGMENT_GAP = 2;

function niceMax(value: number): number {
  if (value <= 0) return 100;
  const exp = 10 ** Math.floor(Math.log10(value / 4));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * exp).find((s) => s * 4 >= value) ?? 10 * exp;
  return step * 4;
}

// Rounded 4px data end, square at the baseline.
function columnPath(x: number, y: number, w: number, h: number, rounded: boolean): string {
  const r = rounded ? Math.min(4, w / 2, h) : 0;
  return `M${x},${y + h}V${y + r}Q${x},${y} ${x + r},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${y + h}Z`;
}

// Column chart on one axis with a recessive grid and a per-column hover/focus tooltip; optionally stacked.
export function ColumnChart({ columns, label, height = 190, reference }: Props) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(600);
  const [active, setActive] = useState<number | null>(null);

  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const ro = new ResizeObserver(([entry]) => setWidth(Math.max(240, entry.contentRect.width)));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const max = niceMax(Math.max(...columns.map((c) => c.value), reference?.value ?? 0));
  const plotW = width - PAD.left - PAD.right;
  const plotH = height - PAD.top - PAD.bottom;
  const band = plotW / Math.max(columns.length, 1);
  const barW = Math.max(2, Math.min(24, band - 2));
  const y = (v: number) => PAD.top + plotH - (v / max) * plotH;
  const ticks = [0, 0.25, 0.5, 0.75, 1].map((f) => f * max);

  const onKeyDown = (e: KeyboardEvent<SVGSVGElement>) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    e.preventDefault();
    const start = active ?? (e.key === 'ArrowRight' ? -1 : columns.length);
    setActive(Math.max(0, Math.min(columns.length - 1, start + (e.key === 'ArrowRight' ? 1 : -1))));
  };

  const activeCol = active != null ? columns[active] : null;
  const tipX = active != null ? PAD.left + band * active + band / 2 : 0;

  const renderColumn = (c: Column, i: number) => {
    const x = PAD.left + band * i + (band - barW) / 2;
    const cls = `chart-col ${c.muted ? 'chart-col-muted' : ''} ${c.emphasis ? 'chart-col-emphasis' : ''} ${active === i ? 'chart-col-active' : ''}`;
    if (c.value <= 0) return null;

    if (!c.segments?.length) {
      const top = y(c.value);
      return <path className={cls} d={columnPath(x, top, barW, y(0) - top, true)} />;
    }

    const parts = c.segments.filter((s) => s.value > 0);
    let base = 0;
    return (
      <g className={cls}>
        {parts.map((s, j) => {
          const bottom = y(base);
          base += s.value;
          const top = y(base);
          const gap = j > 0 && bottom - top > SEGMENT_GAP + 1 ? SEGMENT_GAP : 0;
          return <path key={s.key} style={{ fill: s.color }} d={columnPath(x, top, barW, Math.max(0, bottom - top - gap), j === parts.length - 1)} />;
        })}
      </g>
    );
  };

  return (
    <div className="chart" ref={wrapRef}>
      <svg
        width={width}
        height={height}
        role="img"
        aria-label={label}
        tabIndex={0}
        onKeyDown={onKeyDown}
        onBlur={() => setActive(null)}
        onPointerLeave={() => setActive(null)}
      >
        {ticks.map((t) => (
          <g key={t}>
            <line className="chart-grid" x1={PAD.left} x2={width - PAD.right} y1={y(t)} y2={y(t)} />
            <text className="chart-tick" x={PAD.left - 6} y={y(t)} dy="0.32em" textAnchor="end">
              {compactMoney(t)}
            </text>
          </g>
        ))}

        {columns.map((c, i) => (
          <g key={c.key} onPointerEnter={() => setActive(i)} onPointerDown={() => setActive(i)}>
            <rect className="chart-hit" x={PAD.left + band * i} y={PAD.top} width={band} height={plotH} />
            {renderColumn(c, i)}
            {c.tick && (
              <text
                className={`chart-tick ${c.emphasis ? 'chart-tick-strong' : ''}`}
                x={PAD.left + band * i + band / 2}
                y={height - 8}
                textAnchor="middle"
              >
                {c.tick}
              </text>
            )}
          </g>
        ))}

        <line className="chart-baseline" x1={PAD.left} x2={width - PAD.right} y1={y(0)} y2={y(0)} />

        {reference && reference.value > 0 && (
          <g pointerEvents="none">
            <line className="chart-reference" x1={PAD.left} x2={width - PAD.right} y1={y(reference.value)} y2={y(reference.value)} />
            <text className="chart-reference-label" x={width - PAD.right} y={y(reference.value) - 5} textAnchor="end">
              {reference.label}
            </text>
          </g>
        )}
      </svg>

      {activeCol && (
        <div className="chart-tooltip" style={{ left: Math.min(Math.max(tipX, 80), width - 80), top: Math.max(0, y(activeCol.value) - 8) }}>
          <strong>{money(activeCol.value, { dash: false })}</strong>
          <span>{activeCol.title}</span>
          {(activeCol.rows ?? activeCol.segments?.filter((s) => s.value > 0).reverse().map((s) => ({ label: s.label, value: money(s.value), color: s.color })))?.map((r) => (
            <span key={r.label} className="chart-tooltip-row">
              {r.color && <span className="line-key" style={{ background: r.color }} aria-hidden="true" />}
              {r.label} <b>{r.value}</b>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
