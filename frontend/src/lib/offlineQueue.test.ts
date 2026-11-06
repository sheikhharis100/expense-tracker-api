import { describe, expect, it } from 'vitest';
import type { ExpenseInput } from '../api/types';
import { createOfflineQueue, enqueue, isRejection, parseStored, type QueuedExpense, type QueueStorage } from './offlineQueue';

const input = (clientRequestId: string, amount = 100): ExpenseInput & { clientRequestId: string } => ({
  clientRequestId,
  date: '2026-11-06',
  amount,
  kind: 'Expense',
  status: 'Paid',
  categoryId: 3,
  description: 'groceries',
  paymentMethod: 'Cash',
  isAdditional: false,
});

function memoryStorage(initial: unknown = []) {
  let stored: unknown = initial;
  const storage: QueueStorage & { stored: () => unknown } = {
    load: () => stored,
    save: (items) => {
      stored = JSON.parse(JSON.stringify(items));
      return true;
    },
    stored: () => stored,
  };
  return storage;
}

const httpError = (status: number, message = 'Category not found') => Object.assign(new Error(message), { status });
const networkError = () => new TypeError('Failed to fetch');
const now = () => new Date('2026-11-06T10:00:00Z');

describe('enqueue', () => {
  it('adds a waiting item with its id, input and time', () => {
    const items = enqueue([], input('a'), now());
    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({ clientRequestId: 'a', status: 'waiting', createdAt: '2026-11-06T10:00:00.000Z' });
    expect(items[0].input.amount).toBe(100);
  });

  it('keeps one copy per id', () => {
    const items = enqueue(enqueue([], input('a', 100), now()), input('a', 250), now());
    expect(items).toHaveLength(1);
    expect(items[0].input.amount).toBe(250);
  });

  it('persists through the store and survives a reload', () => {
    const storage = memoryStorage();
    const q = createOfflineQueue({ storage, send: async () => {}, now });
    expect(q.enqueue(input('a'))).toBe(true);
    const again = createOfflineQueue({ storage, send: async () => {}, now });
    expect(again.getSnapshot().items.map((i) => i.clientRequestId)).toEqual(['a']);
  });

  it('reports when the browser refused to store it', () => {
    const q = createOfflineQueue({ storage: { load: () => null, save: () => false }, send: async () => {}, now });
    expect(q.enqueue(input('a'))).toBe(false);
    expect(q.getSnapshot().items).toHaveLength(1);
  });
});

describe('flush', () => {
  it('removes items the server accepted', async () => {
    const storage = memoryStorage();
    const q = createOfflineQueue({ storage, send: async () => ({ id: 1 }), now });
    q.enqueue(input('a'));
    q.enqueue(input('b'));
    const result = await q.flush();
    expect(result).toEqual({ sent: 2, failed: 0, remaining: 0 });
    expect(q.getSnapshot().items).toHaveLength(0);
    expect(storage.stored()).toEqual([]);
  });

  it('sends each item with its original clientRequestId, oldest first', async () => {
    const sent: string[] = [];
    const q = createOfflineQueue({ storage: memoryStorage(), send: async (item) => void sent.push(item.clientRequestId), now });
    q.enqueue(input('first'));
    q.enqueue(input('second'));
    await q.flush();
    expect(sent).toEqual(['first', 'second']);
  });

  it('marks a 4xx as failed with the server message and carries on', async () => {
    const q = createOfflineQueue({
      storage: memoryStorage(),
      send: async (item) => {
        if (item.clientRequestId === 'bad') throw httpError(400);
      },
      now,
    });
    q.enqueue(input('bad'));
    q.enqueue(input('good'));
    const result = await q.flush();
    expect(result).toEqual({ sent: 1, failed: 1, remaining: 0 });
    expect(q.getSnapshot().items).toEqual([expect.objectContaining({ clientRequestId: 'bad', status: 'failed', error: 'Category not found' })]);
  });

  it('keeps items on a network error and stops trying for now', async () => {
    let calls = 0;
    const q = createOfflineQueue({
      storage: memoryStorage(),
      send: async () => {
        calls++;
        throw networkError();
      },
      now,
    });
    q.enqueue(input('a'));
    q.enqueue(input('b'));
    const result = await q.flush();
    expect(calls).toBe(1);
    expect(result).toEqual({ sent: 0, failed: 0, remaining: 2 });
    expect(q.getSnapshot().items.map((i) => [i.clientRequestId, i.status])).toEqual([
      ['a', 'waiting'],
      ['b', 'waiting'],
    ]);
  });

  it('keeps items waiting on 5xx and 401', async () => {
    for (const status of [500, 503, 401]) {
      const q = createOfflineQueue({ storage: memoryStorage(), send: async () => Promise.reject(httpError(status)), now });
      q.enqueue(input('a'));
      await q.flush();
      expect(q.getSnapshot().items[0].status).toBe('waiting');
    }
  });

  it('keeps the id when retrying a failed item, then removes it once accepted', async () => {
    const seen: string[] = [];
    let reject = true;
    const q = createOfflineQueue({
      storage: memoryStorage(),
      send: async (item) => {
        seen.push(item.clientRequestId);
        if (reject) throw httpError(422, 'Pick a category');
      },
      now,
    });
    q.enqueue(input('same-id'));
    await q.flush();
    expect(q.getSnapshot().items[0]).toMatchObject({ status: 'failed', error: 'Pick a category' });
    // Failed items are not resent on their own.
    await q.flush();
    expect(seen).toEqual(['same-id']);
    reject = false;
    await q.retry('same-id');
    expect(seen).toEqual(['same-id', 'same-id']);
    expect(q.getSnapshot().items).toHaveLength(0);
  });

  it('does not lose entries queued while a flush is running', async () => {
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    const q = createOfflineQueue({ storage: memoryStorage(), send: () => gate, now });
    q.enqueue(input('a'));
    const flushing = q.flush();
    expect(q.getSnapshot()).toMatchObject({ syncing: true, sending: 'a' });
    q.enqueue(input('b'));
    release();
    await flushing;
    expect(q.getSnapshot()).toMatchObject({ items: [], syncing: false, sending: null });
  });

  it('shares one pass between overlapping calls', async () => {
    let calls = 0;
    const q = createOfflineQueue({
      storage: memoryStorage(),
      send: async () => {
        calls++;
      },
      now,
    });
    q.enqueue(input('a'));
    await Promise.all([q.flush(), q.flush(), q.flush()]);
    expect(calls).toBe(1);
  });

  it('discards an item', () => {
    const q = createOfflineQueue({ storage: memoryStorage(), send: async () => {}, now });
    q.enqueue(input('a'));
    q.discard('a');
    expect(q.getSnapshot().items).toHaveLength(0);
  });
});

describe('helpers', () => {
  it('treats only lasting 4xx answers as rejections', () => {
    expect(isRejection(httpError(400))).toBe(true);
    expect(isRejection(httpError(409))).toBe(true);
    expect(isRejection(httpError(401))).toBe(false);
    expect(isRejection(httpError(429))).toBe(false);
    expect(isRejection(httpError(503))).toBe(false);
    expect(isRejection(networkError())).toBe(false);
    expect(isRejection(null)).toBe(false);
  });

  it('ignores damaged storage', () => {
    const ok: QueuedExpense = { clientRequestId: 'a', input: input('a'), createdAt: '2026-11-06T10:00:00Z', status: 'waiting' };
    expect(parseStored('nope')).toEqual([]);
    expect(parseStored([null, { clientRequestId: 1 }, ok])).toEqual([ok]);
  });
});
