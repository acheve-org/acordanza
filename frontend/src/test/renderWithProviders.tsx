import type { ReactElement, ReactNode } from 'react';
import { render, type RenderOptions } from '@testing-library/react';
import { MemoryRouter, type InitialEntry } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from '../context/AuthContext';
import { AuthContext, type AuthContextValue } from '../context/auth-context';

const defaultAuthValue: AuthContextValue = {
  id: 'user-1',
  username: 'alex@example.com',
  name: 'Alex',
  role: 'user',
  permissions: [],
  isFirstAscensionist: false,
  isAreaExpert: false,
  loading: false,
  sessionExpired: false,
  hasPermission: () => false,
  login: async () => null,
  beginPasskeyLogin: async () => {
    throw new Error('beginPasskeyLogin is not implemented by the default test auth value');
  },
  completeLoginWithPasskey: async () => {},
  logout: () => {},
  updateName: () => {},
};

// Merges overrides over a logged-in-as-plain-user default, and derives hasPermission from
// `permissions` unless the caller supplies its own (e.g. to spy on calls).
export function makeAuthValue(overrides: Partial<AuthContextValue> = {}): AuthContextValue {
  const permissions = overrides.permissions ?? defaultAuthValue.permissions;
  return {
    ...defaultAuthValue,
    hasPermission: (permission: string) => permissions.includes(permission),
    ...overrides,
  };
}

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      // retry: false so a mocked-error test fails fast instead of retrying for seconds.
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  });
}

interface RenderWithProvidersOptions extends Omit<RenderOptions, 'wrapper'> {
  /** Plain paths, or `{ pathname, state, ... }` objects when a route needs router state
   * (e.g. the background-location pattern's `backgroundLocation`) already attached. */
  initialEntries?: InitialEntry[];
  queryClient?: QueryClient;
  /** Auth context value overrides, applied over a logged-in-as-plain-user default. Ignored when `useRealAuthProvider` is set. */
  auth?: Partial<AuthContextValue>;
  /**
   * Mounts the real AuthProvider (localStorage + GET /auth/me) instead of supplying a
   * context value directly — the only way to exercise the bootstrap/login/logout state
   * machine itself. `auth` is ignored in this mode since the provider computes its own state.
   */
  useRealAuthProvider?: boolean;
}

export function renderWithProviders(
  ui: ReactElement,
  {
    initialEntries = ['/'],
    queryClient,
    auth,
    useRealAuthProvider = false,
    ...renderOptions
  }: RenderWithProvidersOptions = {},
) {
  const client = queryClient ?? createTestQueryClient();
  const authValue = makeAuthValue(auth);

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={initialEntries}>
          {useRealAuthProvider ? (
            <AuthProvider>{children}</AuthProvider>
          ) : (
            <AuthContext.Provider value={authValue}>{children}</AuthContext.Provider>
          )}
        </MemoryRouter>
      </QueryClientProvider>
    );
  }

  return { ...render(ui, { wrapper: Wrapper, ...renderOptions }), queryClient: client };
}
