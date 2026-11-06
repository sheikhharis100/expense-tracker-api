import { useSyncExternalStore } from 'react';
import type { QueryClient } from '@tanstack/react-query';
import { api, ApiError, currentUserId, NetworkError, onTokenChange } from './client';
import { invalidateFor, useCreateExpense } from './hooks';
import type { ExpenseInput, ExpenseRow } from './types';
import { createOfflineQueue } from '../lib/offlineQueue';
import { toast } from '../components/Toast';

const QUEUE_KEY = 'expense-tracker.offline-queue';
// One queue per account, so entries saved offline only ever sync to the account that saved them.
const queueKey = () => `${QUEUE_KEY}.${currentUserId() ?? 'signed-out'}`;

export const QUEUED_MESSAGE = 'Saved on this phone — it will sync when you’re back online';
const MEMORY_ONLY_MESSAGE = 'Saved for now — keep the app open until it syncs (this browser won’t store it)';

export const offlineQueue = createOfflineQueue({
  storage: {
    load() {
      try {
        const raw = localStorage.getItem(queueKey());
        return raw ? JSON.parse(raw) : [];
      } catch {
        return [];
      }
    },
    save(items) {
      try {
        if (items.length) localStorage.setItem(queueKey(), JSON.stringify(items));
        else localStorage.removeItem(queueKey());
        return true;
      } catch {
        return false;
      }
    },
  },
  // The original clientRequestId makes a repeat safe: the server returns the entry it already saved.
  send: (item) => api<ExpenseRow>('/api/expenses', { method: 'POST', body: { ...item.input, clientRequestId: item.clientRequestId } }),
});

// Signing in as someone else switches to their queue; the previous account's entries wait for it.
onTokenChange(() => offlineQueue.reload());

export const isQueueKey = (key: string | null) => key === queueKey();

// The request never reached the app: no connection, or the host's gateway answered while the server was down/asleep.
export function isOfflineFailure(err: unknown): boolean {
  if (err instanceof ApiError) return err.status === 502 || err.status === 503 || err.status === 504;
  return err instanceof NetworkError || (typeof navigator !== 'undefined' && navigator.onLine === false);
}

export const useOfflineQueue = () => useSyncExternalStore(offlineQueue.subscribe, offlineQueue.getSnapshot);

// Sends what's waiting (after putting `retryId` back in line, if given), then refreshes every figure the new entries touch.
export async function syncOfflineQueue(qc: QueryClient, retryId?: string) {
  const result = await (retryId ? offlineQueue.retry(retryId) : offlineQueue.flush());
  if (result.sent > 0) {
    invalidateFor(qc);
    toast(`Synced ${result.sent} saved entr${result.sent === 1 ? 'y' : 'ies'}`, 'success');
  }
  return result;
}

// Creates an expense, or keeps it on the phone when there's no way to reach the server right now.
export function useCreateOrQueue() {
  const create = useCreateExpense();
  const save = async (input: ExpenseInput & { clientRequestId: string }): Promise<'saved' | 'queued'> => {
    if (typeof navigator !== 'undefined' && navigator.onLine === false) return queue(input);
    try {
      await create.mutateAsync(input);
      return 'saved';
    } catch (err) {
      if (isOfflineFailure(err)) return queue(input);
      throw err;
    }
  };
  const queue = (input: ExpenseInput & { clientRequestId: string }) => {
    offlineQueue.enqueue(input);
    return 'queued' as const;
  };
  return { save, queue, isPending: create.isPending };
}

export const queuedMessage = () => (offlineQueue.getSnapshot().persisted ? QUEUED_MESSAGE : MEMORY_ONLY_MESSAGE);
