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
