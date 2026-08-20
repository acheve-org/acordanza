import { useEffect, useState, type ReactNode } from 'react';
import { get, type CredentialRequestOptionsJSON } from '@github/webauthn-json';
import { api, setAuthToken, setUnauthorizedHandler } from '../api/client';
import { queryClient } from '../queryClient';
import type { LoginResponse, MeResponse } from '../api/types';
import { AuthContext, type PasskeyChallenge } from './auth-context';

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

export function AuthProvider({ children }: { children: ReactNode }) {
  const [id, setId] = useState<string | null>(null);
  const [username, setUsername] = useState<string | null>(null);
  const [name, setName] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [isFirstAscensionist, setIsFirstAscensionist] = useState(false);
  const [isAreaExpert, setIsAreaExpert] = useState(false);
  const [loading, setLoading] = useState(true);
  // Distinguishes "the session ran out underneath you" from "you pressed Cerrar sesión",
  // which look identical once the state is cleared — LoginScreen says so rather than
  // leaving the user to guess why they're back here.
  const [sessionExpired, setSessionExpired] = useState(false);

  // Declared before the bootstrap effect below so it is registered by the time that
  // effect's GET /auth/me can come back 401. Any request carrying a token that the API
  // rejects ends the session here instead of surfacing as one more failed panel (see
  // client.ts).
  useEffect(() => {
    setUnauthorizedHandler(() => {
      clearSession();
      setSessionExpired(true);
    });
    return () => setUnauthorizedHandler(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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
    setSessionExpired(false);
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

  // The state half of ending a session, shared by the logout button and the expired-session
  // path above — everything except deciding why it happened.
  function clearSession() {
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

  function logout() {
    clearSession();
    // Leaving by choice: the login screen has nothing to explain.
    setSessionExpired(false);
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
        sessionExpired,
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
