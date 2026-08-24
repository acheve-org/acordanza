import { useState } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeMeResponse } from '../test/factories';
import { queryClient } from '../queryClient';
import { api } from '../api/client';
import { useAuth } from '../hooks/useAuth';

const { identifyMock, resetAnalyticsMock } = vi.hoisted(() => ({
  identifyMock: vi.fn(),
  resetAnalyticsMock: vi.fn(),
}));
vi.mock('../lib/analytics', () => ({
  init: vi.fn(),
  identify: identifyMock,
  reset: resetAnalyticsMock,
  trackPageView: vi.fn(),
}));

const STORAGE_KEY = 'acordanza.auth';

const storedSession = (token: string) =>
  JSON.stringify({
    token,
    id: 'user-1',
    username: 'alex@example.com',
    name: 'Alex',
    role: 'user',
    permissions: [],
    isFirstAscensionist: false,
    isAreaExpert: false,
  });

function Probe() {
  const { username, loading, logout, sessionExpired } = useAuth();
  const [requestError, setRequestError] = useState('');
  return (
    <div>
      <span data-testid="probe">{loading ? 'loading' : (username ?? 'logged-out')}</span>
      {/* 'active', not 'not-expired': toHaveTextContent does a substring match, and
          "not-expired" contains "expired" — an assertion for the latter would pass either
          way, which is exactly the vacuous test this file is trying not to have. */}
      <span data-testid="expired">{sessionExpired ? 'expired' : 'active'}</span>
      <span data-testid="request-error">{requestError}</span>
      <button onClick={logout}>Cerrar sesión</button>
      <button onClick={() => api.get('/routes').catch((err: Error) => setRequestError(err.message))}>Cargar vías</button>
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

  // SEC-4, client half: the JWT is long-lived and nothing refreshes it, so a 401 mid-session
  // is the session ending. Before this, every panel on the page just showed its own
  // "Error en la solicitud (401)" while the app still believed it was logged in.
  it('signs out when the API rejects a request that carried a token', async () => {
    const user = userEvent.setup();
    localStorage.setItem(STORAGE_KEY, storedSession('good-token'));
    server.use(
      http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ username: 'alex@example.com' }))),
      http.get(`${API_URL}/routes`, () => HttpResponse.json({ error: 'No autorizado' }, { status: 401 })),
    );

    renderWithProviders(<Probe />, { useRealAuthProvider: true });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('alex@example.com'));

    await user.click(screen.getByRole('button', { name: 'Cargar vías' }));

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('logged-out'));
    expect(screen.getByTestId('expired')).toHaveTextContent('expired');
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    // The caller still learns the request failed — and in words worth reading, rather than
    // the generic status-code fallback.
    expect(screen.getByTestId('request-error')).toHaveTextContent('Tu sesión ha caducado');
  });

  // Same 401, opposite meaning: the bootstrap fetch is how a stale stored token gets found
  // out, and arriving at the login screen with no explanation is what makes that read as a
  // bug rather than as an expiry.
  it('reports an expired session when the stored token is rejected on boot', async () => {
    localStorage.setItem(STORAGE_KEY, storedSession('stale-token'));
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json({ error: 'No autorizado' }, { status: 401 })));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('logged-out'));
    expect(screen.getByTestId('expired')).toHaveTextContent('expired');
  });

  // Leaving on purpose is not an expiry, and the login screen shouldn't claim it was.
  it('does not report an expired session after an explicit logout', async () => {
    const user = userEvent.setup();
    localStorage.setItem(STORAGE_KEY, storedSession('good-token'));
    server.use(
      http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ username: 'alex@example.com' }))),
    );

    renderWithProviders(<Probe />, { useRealAuthProvider: true });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('alex@example.com'));

    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }));

    expect(screen.getByTestId('probe')).toHaveTextContent('logged-out');
    expect(screen.getByTestId('expired')).toHaveTextContent('active');
  });

  it("logout() clears the shared react-query cache", async () => {
    const user = userEvent.setup();
    queryClient.setQueryData(['probe-test-key'], 'cached-value');

    renderWithProviders(<Probe />, { useRealAuthProvider: true });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('logged-out'));

    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }));

    expect(queryClient.getQueryData(['probe-test-key'])).toBeUndefined();
  });

  it('identifies the user in Amplitude after a successful session restore', async () => {
    localStorage.setItem(STORAGE_KEY, storedSession('good-token'));
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ id: 'user-9' }))));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });

    await waitFor(() => expect(identifyMock).toHaveBeenCalledWith('user-9'));
  });

  it('resets Amplitude identity on logout', async () => {
    const user = userEvent.setup();
    localStorage.setItem(STORAGE_KEY, storedSession('good-token'));
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ id: 'user-9' }))));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('alex@example.com'));

    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }));

    expect(resetAnalyticsMock).toHaveBeenCalled();
  });
});
