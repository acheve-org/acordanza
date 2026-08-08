import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeMeResponse } from '../test/factories';
import { queryClient } from '../queryClient';
import { useAuth } from './AuthContext';

const STORAGE_KEY = 'acordanza.auth';

function Probe() {
  const { username, loading, logout } = useAuth();
  return (
    <div>
      <span data-testid="probe">{loading ? 'loading' : (username ?? 'logged-out')}</span>
      <button onClick={logout}>Cerrar sesión</button>
    </div>
  );
}

describe('AuthProvider', () => {
  it('drops a stored token silently when GET /auth/me rejects it', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        token: 'stale-token',
        id: 'user-1',
        username: 'alex@example.com',
        name: 'Alex',
        role: 'user',
        permissions: [],
        isFirstAscensionist: false,
        isAreaExpert: false,
      }),
    );
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json({ error: 'No autorizado' }, { status: 401 })));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('logged-out'));
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('keeps a stored session when GET /auth/me accepts the token', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        token: 'good-token',
        id: 'user-1',
        username: 'alex@example.com',
        name: 'Alex',
        role: 'user',
        permissions: [],
        isFirstAscensionist: false,
        isAreaExpert: false,
      }),
    );
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ username: 'alex@example.com' }))));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('alex@example.com'));
  });

  it("logout() clears the shared react-query cache", async () => {
    const user = userEvent.setup();
    queryClient.setQueryData(['probe-test-key'], 'cached-value');

    renderWithProviders(<Probe />, { useRealAuthProvider: true });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('logged-out'));

    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }));

    expect(queryClient.getQueryData(['probe-test-key'])).toBeUndefined();
  });
});
