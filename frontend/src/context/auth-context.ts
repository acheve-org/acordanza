import { createContext } from 'react';

export interface PasskeyChallenge {
  sessionId: string;
  options: unknown;
}

export interface AuthContextValue {
  id: string | null;
  username: string | null;
  name: string | null;
  role: string | null;
  permissions: string[];
  isFirstAscensionist: boolean;
  isAreaExpert: boolean;
  loading: boolean;
  /** True when the session ended because the API rejected the token, rather than because
   * the user pressed "Cerrar sesión" — the two are indistinguishable once state is cleared,
   * and only the first is worth explaining on the login screen. */
  sessionExpired: boolean;
  hasPermission: (permission: string) => boolean;
  login: (email: string, password: string) => Promise<PasskeyChallenge | null>;
  beginPasskeyLogin: () => Promise<PasskeyChallenge>;
  completeLoginWithPasskey: (challenge: PasskeyChallenge) => Promise<void>;
  logout: () => void;
  updateName: (name: string) => void;
}

// Exported so tests can supply a value directly (see src/test/renderWithProviders.tsx)
// instead of going through the real provider's localStorage/GET-/auth/me bootstrap.
export const AuthContext = createContext<AuthContextValue | null>(null);
