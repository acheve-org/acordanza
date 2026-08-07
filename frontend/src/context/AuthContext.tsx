import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { get, type CredentialRequestOptionsJSON } from '@github/webauthn-json';
import { api, setAuthToken } from '../api/client';
import { queryClient } from '../queryClient';
import type { LoginResponse, MeResponse } from '../api/types';

const STORAGE_KEY = 'acordanza.auth';

interface StoredAuth {
  token: string;
  id: string;
  username: string;
  name: string;
  role: string;
  permissions: string[];
  isFirstAscensionist: boolean;
  isAreaExpert: boolean;
}

export interface PasskeyChallenge {
  sessionId: string;
  options: unknown;
}

interface AuthContextValue {
  id: string | null;
  username: string | null;
  name: string | null;
  role: string | null;
  permissions: string[];
  isFirstAscensionist: boolean;
  isAreaExpert: boolean;
  loading: boolean;
  hasPermission: (permission: string) => boolean;
  login: (email: string, password: string) => Promise<PasskeyChallenge | null>;
  beginPasskeyLogin: () => Promise<PasskeyChallenge>;
  completeLoginWithPasskey: (challenge: PasskeyChallenge) => Promise<void>;
  logout: () => void;
  updateName: (name: string) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [id, setId] = useState<string | null>(null);
  const [username, setUsername] = useState<string | null>(null);
  const [name, setName] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [isFirstAscensionist, setIsFirstAscensionist] = useState(false);
  const [isAreaExpert, setIsAreaExpert] = useState(false);
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
      .get<MeResponse>('/auth/me')
      .then((me) => {
        setId(me.id);
        setUsername(me.username);
        setName(me.name);
        setRole(me.role);
        setPermissions(me.permissions);
        setIsFirstAscensionist(me.isFirstAscensionist);
        setIsAreaExpert(me.isAreaExpert);
      })
      .catch(() => {
        localStorage.removeItem(STORAGE_KEY);
        setAuthToken(null);
      })
      .finally(() => setLoading(false));
  }, []);

  async function applySession(token: string) {
    // Drop any query-cache data fetched under a previous identity (or no
    // identity) before this one starts issuing requests of its own.
    queryClient.clear();
    setAuthToken(token);
    const me = await api.get<MeResponse>('/auth/me');
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        token,
        id: me.id,
        username: me.username,
        name: me.name,
        role: me.role,
        permissions: me.permissions,
        isFirstAscensionist: me.isFirstAscensionist,
        isAreaExpert: me.isAreaExpert,
      }),
    );
    setId(me.id);
    setUsername(me.username);
    setName(me.name);
    setRole(me.role);
    setPermissions(me.permissions);
    setIsFirstAscensionist(me.isFirstAscensionist);
    setIsAreaExpert(me.isAreaExpert);
  }

  async function login(email: string, password: string): Promise<PasskeyChallenge | null> {
    const res = await api.post<LoginResponse>('/auth/login', { email, password });
    if (res.mfaRequired && res.sessionId) {
      return { sessionId: res.sessionId, options: res.options };
    }
    await applySession(res.token!);
    return null;
  }

  async function beginPasskeyLogin(): Promise<PasskeyChallenge> {
    const res = await api.post<LoginResponse>('/auth/login/passkey/options');
    if (!res.sessionId || !res.options) {
      throw new Error('Respuesta inesperada del servidor');
    }
    return { sessionId: res.sessionId, options: res.options };
  }

  async function completeLoginWithPasskey(challenge: PasskeyChallenge) {
    const assertionResponse = await get({
      publicKey: challenge.options,
    } as CredentialRequestOptionsJSON);
    const res = await api.post<LoginResponse>('/auth/login/passkey', {
      sessionId: challenge.sessionId,
      assertionResponse,
    });
    await applySession(res.token!);
  }

  function logout() {
    localStorage.removeItem(STORAGE_KEY);
    setAuthToken(null);
    queryClient.clear();
    setId(null);
    setUsername(null);
    setName(null);
    setRole(null);
    setPermissions([]);
    setIsFirstAscensionist(false);
    setIsAreaExpert(false);
  }

  function hasPermission(permission: string) {
    return permissions.includes(permission);
  }

  function updateName(newName: string) {
    setName(newName);
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (!stored) return;
      const parsed = JSON.parse(stored) as StoredAuth;
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...parsed, name: newName }));
    } catch {
      // Storage unavailable/over quota: in-memory state is already updated,
      // so the save still succeeded from the user's perspective this session.
    }
  }

  return (
    <AuthContext.Provider
      value={{
        id,
        username,
        name,
        role,
        permissions,
        isFirstAscensionist,
        isAreaExpert,
        loading,
        hasPermission,
        login,
        beginPasskeyLogin,
        completeLoginWithPasskey,
        logout,
        updateName,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
