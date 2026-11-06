import { useSyncExternalStore } from 'react';

type Tone = 'info' | 'error' | 'success';
interface ToastItem {
  id: number;
  message: string;
  tone: Tone;
}

let items: ToastItem[] = [];
let nextId = 1;
const subscribers = new Set<() => void>();
const emit = () => subscribers.forEach((fn) => fn());

export function toast(message: string, tone: Tone = 'info') {
  const id = nextId++;
  items = [...items.filter((t) => t.message !== message), { id, message, tone }].slice(-3);
  emit();
  window.setTimeout(() => dismiss(id), tone === 'error' ? 6000 : 3000);
}

function dismiss(id: number) {
  items = items.filter((t) => t.id !== id);
  emit();
}

export function Toaster() {
  const list = useSyncExternalStore(
    (cb) => {
      subscribers.add(cb);
      return () => subscribers.delete(cb);
    },
    () => items,
  );

  return (
    <div className="toaster" role="status" aria-live="polite">
      {list.map((t) => (
        <div key={t.id} className={`toast toast-${t.tone}`}>
          <span>{t.message}</span>
          <button type="button" className="toast-close" aria-label="Dismiss" onClick={() => dismiss(t.id)}>
            ×
          </button>
        </div>
      ))}
    </div>
  );
}
