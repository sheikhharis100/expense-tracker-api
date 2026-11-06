import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { clientId, getToken } from '../api/client';
import { invalidateFor } from '../api/hooks';
import { syncOfflineQueue } from '../api/offline';
import type { LedgerChange } from '../api/types';

export type LiveStatus = 'connecting' | 'live' | 'offline';

interface LiveState {
  status: LiveStatus;
  // Bumps when another device changes something, so the UI can acknowledge it.
  remoteChangeAt: number;
}

const LiveContext = createContext<LiveState>({ status: 'connecting', remoteChangeAt: 0 });

const backoff = (attempt: number) => Math.min(30_000, 1_000 * 2 ** Math.min(attempt, 5));

// Stopping a connection mid-handshake (page unload, React dev double-mount) is expected, not an error.
const logger = {
  log(level: LogLevel, message: string) {
    if (level < LogLevel.Warning || /stopped during negotiation/i.test(message)) return;
    console.warn(`[live] ${message}`);
  },
};

export function LiveProvider({ token, children }: { token: string | null; children: ReactNode }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<LiveStatus>('connecting');
  const [remoteChangeAt, setRemoteChangeAt] = useState(0);

  useEffect(() => {
    if (!token) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/ledger', { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => backoff(ctx.previousRetryCount) })
      .configureLogging(logger)
      .build();

    connection.on('ledgerChanged', (change: LedgerChange) => {
      if (change.origin === clientId) return;
      invalidateFor(qc, change);
      setRemoteChangeAt(Date.now());
    });
    connection.onreconnecting(() => setStatus('connecting'));
    connection.onreconnected(() => {
      setStatus('live');
      // Anything could have changed while we were away, and entries saved on the phone can go now.
      qc.invalidateQueries();
      void syncOfflineQueue(qc);
    });
    connection.onclose(() => setStatus('offline'));

    let stopped = false;
    let retryTimer = 0;
    const start = async (attempt: number) => {
      try {
        await connection.start();
        if (stopped) return;
        setStatus('live');
        // Connected means the server is reachable: send anything saved on the phone (overlapping flushes share one pass).
        void syncOfflineQueue(qc);
      } catch {
        if (stopped) return;
        setStatus('offline');
        retryTimer = window.setTimeout(() => start(attempt + 1), backoff(attempt + 1));
      }
    };

    setStatus('connecting');
    start(0);

    // Phones suspend sockets in the background; reconnect as soon as the app is visible again.
    const onVisible = () => {
      if (document.visibilityState === 'visible' && connection.state === HubConnectionState.Disconnected && !stopped) {
        window.clearTimeout(retryTimer);
        start(0);
      }
    };
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      stopped = true;
      window.clearTimeout(retryTimer);
      document.removeEventListener('visibilitychange', onVisible);
      connection.stop();
    };
  }, [token, qc]);

  return <LiveContext.Provider value={{ status, remoteChangeAt }}>{children}</LiveContext.Provider>;
}

export const useLive = () => useContext(LiveContext);
