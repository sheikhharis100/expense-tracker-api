import type { ExpenseInput } from '../api/types';

// New expenses that couldn't reach the server (no signal, server asleep) wait here and are re-sent later with their
// original clientRequestId, so the server answers a repeat with the entry it already has instead of a duplicate.
// No browser globals: storage and the sender are injected so the logic is testable on its own.

export type QueueStatus = 'waiting' | 'failed';

export interface QueuedExpense {
  clientRequestId: string;
  input: ExpenseInput;
  createdAt: string;
  status: QueueStatus;
  // The server's reason when it refused the entry (4xx).
  error?: string;
}

export interface QueueStorage {
  load(): unknown;
  // False when the browser refused to store it (private mode, quota): the item then lives in memory only.
  save(items: QueuedExpense[]): boolean;
}

export interface QueueSnapshot {
  items: readonly QueuedExpense[];
  syncing: boolean;
  // The entry on its way to the server right now, if any.
  sending: string | null;
  persisted: boolean;
}

export interface FlushResult {
  sent: number;
  failed: number;
  remaining: number;
}

// A 4xx means the server read the entry and refused it; sending it again won't help. 401 (signed out), 408 and 429
// are about the moment, not the entry, so those wait like a network error or a 5xx.
export function isRejection(err: unknown): boolean {
  const status = (err as { status?: unknown } | null)?.status;
  return typeof status === 'number' && status >= 400 && status < 500 && status !== 401 && status !== 408 && status !== 429;
}

export function enqueue(items: readonly QueuedExpense[], input: ExpenseInput & { clientRequestId: string }, now: Date): QueuedExpense[] {
  const { clientRequestId, ...rest } = input;
  const item: QueuedExpense = { clientRequestId, input: rest, createdAt: now.toISOString(), status: 'waiting' };
  // The same id twice is the same entry; keep one copy.
  return [...items.filter((i) => i.clientRequestId !== clientRequestId), item];
}

export function parseStored(raw: unknown): QueuedExpense[] {
  if (!Array.isArray(raw)) return [];
  return raw.filter(
    (i): i is QueuedExpense =>
      !!i && typeof i.clientRequestId === 'string' && typeof i.createdAt === 'string' && !!i.input && typeof i.input.amount === 'number',
  ).map((i) => ({ ...i, status: i.status === 'failed' ? 'failed' : 'waiting' }));
}

export function createOfflineQueue({
  storage,
  send,
  now = () => new Date(),
}: {
  storage: QueueStorage;
  send: (item: QueuedExpense) => Promise<unknown>;
  now?: () => Date;
}) {
  let snapshot: QueueSnapshot = { items: parseStored(storage.load()), syncing: false, sending: null, persisted: true };
  const subscribers = new Set<() => void>();
  let running: Promise<FlushResult> | null = null;
  let again = false;

  const emit = () => subscribers.forEach((fn) => fn());
  const setItems = (items: QueuedExpense[]) => {
    snapshot = { ...snapshot, items, persisted: storage.save(items) };
    emit();
  };
  const setStatus = (syncing: boolean, sending: string | null) => {
    snapshot = { ...snapshot, syncing, sending };
    emit();
  };
  // Results are applied by id against the latest list, so entries queued during a flush are never lost.
  const update = (id: string, fn: (item: QueuedExpense) => QueuedExpense | null) =>
    setItems(snapshot.items.flatMap((i) => (i.clientRequestId === id ? (fn(i) ?? []) : [i])));

  async function runOnce(): Promise<Omit<FlushResult, 'remaining'>> {
    let sent = 0;
    let failed = 0;
    const tried = new Set<string>();
    for (;;) {
      const next = snapshot.items.find((i) => i.status === 'waiting' && !tried.has(i.clientRequestId));
      if (!next) break;
      tried.add(next.clientRequestId);
      setStatus(true, next.clientRequestId);
      try {
        await send(next);
        update(next.clientRequestId, () => null);
        sent++;
      } catch (err) {
        if (!isRejection(err)) break; // Still offline or the server is struggling: keep it and try again later.
        update(next.clientRequestId, (i) => ({ ...i, status: 'failed', error: err instanceof Error ? err.message : 'Rejected by the server' }));
        failed++;
      }
    }
    setStatus(true, null);
    return { sent, failed };
  }

  function flush(): Promise<FlushResult> {
    if (running) {
      // Asked again mid-flush (e.g. a retry tap): run once more when this pass ends.
      again = true;
      return running;
    }
    running = (async () => {
      const total = { sent: 0, failed: 0 };
      setStatus(true, null);
      try {
        do {
          again = false;
          const r = await runOnce();
          total.sent += r.sent;
          total.failed += r.failed;
        } while (again);
      } finally {
        setStatus(false, null);
        running = null;
      }
      return { ...total, remaining: snapshot.items.filter((i) => i.status === 'waiting').length };
    })();
    return running;
  }

  return {
    subscribe(fn: () => void) {
      subscribers.add(fn);
      return () => {
        subscribers.delete(fn);
      };
    },
    getSnapshot: () => snapshot,
    enqueue(input: ExpenseInput & { clientRequestId: string }) {
      setItems(enqueue(snapshot.items, input, now()));
      return snapshot.persisted;
    },
    retry(id: string) {
      update(id, (i) => ({ ...i, status: 'waiting', error: undefined }));
      return flush();
    },
    discard(id: string) {
      update(id, () => null);
    },
    // Another tab changed the stored queue.
    reload() {
      snapshot = { ...snapshot, items: parseStored(storage.load()) };
      emit();
    },
    flush,
  };
}

export type OfflineQueue = ReturnType<typeof createOfflineQueue>;
