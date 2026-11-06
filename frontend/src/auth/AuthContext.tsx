import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, getToken, onTokenChange, setToken } from '../api/client';
import type { AuthResponse } from '../api/types';

interface AuthState {
  token: string | null;
  signIn: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  // Signs in with a token from an emailed link or a password reset; it may be a different account than before.
  startSession: (res: AuthResponse) => void;
  signOut: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [token, setTokenState] = useState(getToken);

  useEffect(
    () =>
      onTokenChange((next) => {
        setTokenState(next);
        if (!next) qc.clear();
      }),
    [qc],
  );

  const startSession = (res: AuthResponse) => {
    qc.clear();
    setToken(res.token);
  };

  const value: AuthState = {
    token,
    signIn: async (email, password) => {
      startSession(await api<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, auth: false }));
    },
    register: async (email, password, displayName) => {
      startSession(
        await api<AuthResponse>('/api/auth/register', {
          method: 'POST',
          body: { email, password, displayName },
          auth: false,
        }),
      );
    },
    startSession,
    signOut: () => setToken(null),
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
