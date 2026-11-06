import { useEffect, useSyncExternalStore } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useCategories } from '../api/hooks';
import { isQueueKey, offlineQueue, syncOfflineQueue, useOfflineQueue } from '../api/offline';
import { hasSlowRequests, subscribeSlowRequests } from '../api/slowRequests';
import { dayLabel } from '../lib/dates';
import { money } from '../lib/format';
import { Icon } from './Icon';
import { Sheet } from './Sheet';
import { toast } from './Toast';

const RETRY_EVERY_MS = 30_000;

// Sends entries saved on the phone: at start, when the phone comes back online, and every 30 s while any wait.
// The live connection also flushes when it reconnects (see LiveProvider).
export function useOfflineSync() {
  const qc = useQueryClient();
  const waiting = useSyncExternalStore(offlineQueue.subscribe, () => offlineQueue.getSnapshot().items.some((i) => i.status === 'waiting'));

  useEffect(() => {
    const sync = () => void syncOfflineQueue(qc);
    const onStorage = (e: StorageEvent) => isQueueKey(e.key) && offlineQueue.reload();
    sync();
    window.addEventListener('online', sync);
    window.addEventListener('storage', onStorage);
    return () => {
      window.removeEventListener('online', sync);
      window.removeEventListener('storage', onStorage);
    };
  }, [qc]);

  useEffect(() => {
    if (!waiting) return;
    const timer = window.setInterval(() => {
      if (navigator.onLine !== false) void syncOfflineQueue(qc);
    }, RETRY_EVERY_MS);
    return () => window.clearInterval(timer);
  }, [waiting, qc]);
}

export function SyncBadge({ onOpen }: { onOpen: () => void }) {
  const { items } = useOfflineQueue();
  if (items.length === 0) return null;
  const failed = items.filter((i) => i.status === 'failed').length;
  const waiting = items.length - failed;
  const text = [waiting ? `${waiting} waiting to sync` : '', failed ? `${failed} not saved` : ''].filter(Boolean).join(' · ');
  return (
    <button type="button" className={`sync-badge ${failed ? 'sync-badge-failed' : ''}`} onClick={onOpen} aria-label={`${text}. Show details`} title={text}>
      <Icon name={failed ? 'alert' : 'upload'} size={14} />
      <span className="sync-long">{text}</span>
      <span className="sync-short">{items.length}</span>
    </button>
  );
}

export function SyncSheet({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const { items, syncing, sending } = useOfflineQueue();
  const categories = useCategories();
  const categoryName = (id: number | null | undefined) => categories.data?.find((c) => c.id === id)?.name ?? (id ? 'Category' : 'No category');

  const tryNow = async (retryId?: string) => {
    const r = await syncOfflineQueue(qc, retryId);
    if (r.sent === 0 && r.failed === 0 && r.remaining > 0) toast('Still can’t reach the server. It will keep trying.', 'info');
  };

  const discard = (id: string) => {
    if (confirm('Discard this entry? It hasn’t reached the server, so it will be lost.')) offlineQueue.discard(id);
  };

  return (
    <Sheet title="Waiting to sync" onClose={onClose}>
      <div className="form-stack">
        {items.length === 0 ? (
          <p className="muted">Everything on this phone has been saved to the server.</p>
        ) : (
          <>
            <p className="muted small">
              These entries are saved on this phone but haven’t reached the server yet. They’re sent automatically when you’re back online; nothing is counted twice.
            </p>
            <ul className="sync-list">
              {items.map((item) => {
                const busy = sending === item.clientRequestId;
                return (
                  <li key={item.clientRequestId} className={item.status === 'failed' ? 'sync-item-failed' : ''}>
                    <span className="sync-item-main">
                      <span className="sync-item-title">
                        <b>{money(item.input.amount, { dash: false })}</b> · {item.input.kind === 'Transfer' ? 'Transfer' : categoryName(item.input.categoryId)}
                      </span>
                      <span className="muted small">
                        {dayLabel(item.input.date).day}
                        {item.input.description && ` · ${item.input.description}`}
                        {item.input.status === 'Planned' && ' · planned'}
                      </span>
                      {item.status === 'failed' ? (
                        <span className="error-text small">Not saved: {item.error}</span>
                      ) : (
                        <span className="muted small">{busy ? 'Sending…' : 'Waiting'}</span>
                      )}
                    </span>
                    <span className="btn-row">
                      <button type="button" className="btn btn-sm" disabled={busy} onClick={() => void tryNow(item.clientRequestId)}>
                        Retry
                      </button>
                      <button type="button" className="btn btn-sm btn-danger-ghost" disabled={busy} onClick={() => discard(item.clientRequestId)}>
                        Discard
                      </button>
                    </span>
                  </li>
                );
              })}
            </ul>
            <button type="button" className="btn btn-primary" disabled={syncing} onClick={() => void tryNow()}>
              {syncing ? 'Syncing…' : 'Try now'}
            </button>
          </>
        )}
      </div>
    </Sheet>
  );
}

// Shown while any request has waited a few seconds: on the free host that's the server waking up.
export function WakingBanner() {
  const slow = useSyncExternalStore(subscribeSlowRequests, hasSlowRequests);
  if (!slow) return null;
  return (
    <div className="wake-banner" role="status">
      <span className="wake-spinner" aria-hidden="true" />
      Starting the free server… this can take up to a minute. Nothing you enter is lost.
    </div>
  );
}
