import { Icon } from './Icon';

// Fill carries severity: the column's own colour while healthy, then warning, then critical (with icon + label).
export function Meter({ value, max, label, color }: { value: number; max: number; label: string; color?: string }) {
  const pct = max > 0 ? (value / max) * 100 : 0;
  const level = pct > 100 ? 'critical' : pct >= 80 ? 'warning' : 'ok';
  const fill = level === 'ok' && color ? color : undefined;

  return (
    <div className="meter-wrap">
      <div
        className={`meter meter-${level}`}
        role="meter"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(Math.min(pct, 100))}
        style={fill ? { ['--meter-fill' as string]: fill } : undefined}
      >
        <div className="meter-fill" style={{ width: `${Math.min(pct, 100)}%` }} />
      </div>
      <span className={`status-label status-${level}`}>
        {level === 'critical' ? (
          <>
            <Icon name="alert" size={12} /> Over
          </>
        ) : level === 'warning' ? (
          <>
            <Icon name="flame" size={12} /> {Math.round(pct)}%
          </>
        ) : (
          `${Math.round(pct)}%`
        )}
      </span>
    </div>
  );
}
