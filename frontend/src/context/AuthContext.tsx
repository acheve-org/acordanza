import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { get, type CredentialRequestOptionsJSON } from '@github/webauthn-json';
import { api, setAuthToken } from '../api/client';
import type { LoginResponse } from '../api/types';

const STORAGE_KEY = 'acordanza.auth';

interface StoredAuth {
  token: string;
  username: string;
}

export interface PasskeyChallenge {
  sessionId: string;
  options: unknown;
}

interface AuthContextValue {
  username: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<PasskeyChallenge | null>;
  completeLoginWithPasskey: (challenge: PasskeyChallenge) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [username, setUsername] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (!stored) {
      setLoading(false);
      return;
    }

    const parsed = JSON.parse(stored) as StoredAuth;
    setAuthToken(parsed.token);

    api
      .get<{ username: string }>('/auth/me')
      .then((me) => setUsername(me.username))
      .catch(() => {
        localStorage.removeItem(STORAGE_KEY);
        setAuthToken(null);
      })
      .finally(() => setLoading(false));
  }, []);

  function applySession(token: string, sessionUsername: string) {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ token, username: sessionUsername }));
    setAuthToken(token);
    setUsername(sessionUsername);
  }

  async function login(email: string, password: string): Promise<PasskeyChallenge | null> {
    const res = await api.post<LoginResponse>('/auth/login', { email, password });
    if (res.mfaRequired && res.sessionId) {
      return { sessionId: res.sessionId, options: res.options };
    }
    applySession(res.token!, res.username!);
    return null;
  }

  async function completeLoginWithPasskey(challenge: PasskeyChallenge) {
    const assertionResponse = await get({
      publicKey: challenge.options,
    } as CredentialRequestOptionsJSON);
    const res = await api.post<LoginResponse>('/auth/login/passkey', {
      sessionId: challenge.sessionId,
      assertionResponse,
    });
    applySession(res.token!, res.username!);
  }

  function logout() {
    localStorage.removeItem(STORAGE_KEY);
    setAuthToken(null);
    setUsername(null);
  }

  return (
    <AuthContext.Provider value={{ username, loading, login, completeLoginWithPasskey, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
