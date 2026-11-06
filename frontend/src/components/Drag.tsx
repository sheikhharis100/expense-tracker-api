import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { flushSync } from 'react-dom';
import { clamp, dropIndex } from '../lib/reorder';
import { Icon } from './Icon';

export const MIN_COLUMN_WIDTH = 60;
export const MAX_COLUMN_WIDTH = 600;

const clampWidth = (w: number) => Math.round(clamp(w, MIN_COLUMN_WIDTH, MAX_COLUMN_WIDTH));

// Window-level listeners for one drag gesture; they keep working when the finger leaves the handle or React moves
// the element under it.
function followPointer(
  e: ReactPointerEvent,
  bodyClass: 'is-resizing' | 'is-reordering',
  onMove: (ev: PointerEvent) => void,
  onEnd: (committed: boolean) => void,
) {
  const id = e.pointerId;
  const move = (ev: PointerEvent) => ev.pointerId === id && onMove(ev);
  const up = (ev: PointerEvent) => ev.pointerId === id && finish(ev.type === 'pointerup');
  const finish = (committed: boolean) => {
    window.removeEventListener('pointermove', move);
    window.removeEventListener('pointerup', up);
    window.removeEventListener('pointercancel', up);
    document.body.classList.remove(bodyClass);
    onEnd(committed);
  };
  window.addEventListener('pointermove', move);
  window.addEventListener('pointerup', up);
  window.addEventListener('pointercancel', up);
  document.body.classList.add(bodyClass);
  return finish;
}

// Drag handle on the right edge of a table header; reports the width once, on release.
// While dragging it sizes the header cell directly (no re-render of the whole table per move) and shows the width.
// Pointer only: the width fields in the Columns sheet are the keyboard way to do the same.
export function ResizeHandle({ name, onCommit }: { name: string; onCommit: (width: number) => void }) {
  const [width, setWidth] = useState<number | null>(null);
  const cancel = useRef<((committed: boolean) => void) | null>(null);
  useEffect(() => () => cancel.current?.(false), []);

  const onPointerDown = (e: ReactPointerEvent<HTMLSpanElement>) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    const th = e.currentTarget.closest('th');
    if (!th) return;
    const startX = e.clientX;
    const startWidth = th.getBoundingClientRect().width;
    const before = { width: th.style.width, minWidth: th.style.minWidth };
    let current = clampWidth(startWidth);
    let moved = false;
    setWidth(current);
    cancel.current = followPointer(
      e,
      'is-resizing',
      (ev) => {
        moved ||= Math.abs(ev.clientX - startX) > 2;
        current = clampWidth(startWidth + ev.clientX - startX);
        th.style.width = th.style.minWidth = `${current}px`;
        setWidth(current);
      },
      (committed) => {
        cancel.current = null;
        setWidth(null);
        if (committed && moved) return onCommit(current);
        // Put back what React rendered.
        th.style.width = before.width;
        th.style.minWidth = before.minWidth;
      },
    );
  };

  return (
    <span
      className={`col-resize ${width != null ? 'col-resize-active' : ''}`}
      onPointerDown={onPointerDown}
      onClick={(e) => e.stopPropagation()}
      title={`Drag to resize ${name}`}
      aria-hidden="true"
    >
      {width != null && <span className="col-resize-value">{width} px</span>}
    </span>
  );
}

// Reorder a list by dragging a grip. Rows carry data-index; rows swap places live as the pointer passes their middle.
export function useDragReorder(onMove: (from: number, to: number) => void) {
  const listRef = useRef<HTMLUListElement>(null);
  const [dragging, setDragging] = useState<number | null>(null);
  const cancel = useRef<((committed: boolean) => void) | null>(null);
  useEffect(() => () => cancel.current?.(false), []);

  const start = (index: number) => (e: ReactPointerEvent<HTMLElement>) => {
    if (e.button !== 0 || !listRef.current) return;
    e.preventDefault();
    let from = index;
    setDragging(from);
    cancel.current = followPointer(
      e,
      'is-reordering',
      (ev) => {
        const list = listRef.current;
        if (!list) return;
        const scroller = list.closest<HTMLElement>('.sheet-body');
        if (scroller) {
          // Near the edge of a scrolling sheet, scroll so long lists can be reordered end to end.
          const box = scroller.getBoundingClientRect();
          if (ev.clientY < box.top + 40) scroller.scrollTop -= 12;
          else if (ev.clientY > box.bottom - 40) scroller.scrollTop += 12;
        }
        const rows = [...list.querySelectorAll<HTMLElement>(':scope > [data-index]')].map((row) => {
          const r = row.getBoundingClientRect();
          return { index: Number(row.dataset.index), top: r.top, height: r.height };
        });
        const to = dropIndex(from, ev.clientY, rows);
        if (to === from) return;
        // Render now so the next move measures the new order.
        flushSync(() => {
          onMove(from, to);
          setDragging(to);
        });
        from = to;
      },
      () => {
        cancel.current = null;
        setDragging(null);
      },
    );
  };

  const grip = (index: number, name: string) => (
    <span className="drag-grip" onPointerDown={start(index)} title={`Drag to reorder ${name}`} aria-hidden="true">
      <Icon name="grip" size={16} />
    </span>
  );

  return { listRef, dragging, grip };
}
