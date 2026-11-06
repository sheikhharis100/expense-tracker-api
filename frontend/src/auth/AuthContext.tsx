import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, getToken, onTokenChange, setToken } from '../api/client';
import type { AuthResponse } from '../api/types';

interface AuthState {
  token: string | null;
  signIn: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
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

  const value: AuthState = {
    token,
    signIn: async (email, password) => {
      const res = await api<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, auth: false });
      qc.clear();
      setToken(res.token);
    },
    register: async (email, password, displayName) => {
      const res = await api<AuthResponse>('/api/auth/register', {
        method: 'POST',
        body: { email, password, displayName },
        auth: false,
      });
      qc.clear();
      setToken(res.token);
    },
    signOut: () => setToken(null),
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
