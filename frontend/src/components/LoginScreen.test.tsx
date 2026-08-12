import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeLoginResponse, makeMeResponse } from '../test/factories';
import { useAuth } from '../hooks/useAuth';
import { LoginScreen } from './LoginScreen';

const { webauthnGet } = vi.hoisted(() => ({ webauthnGet: vi.fn() }));
vi.mock('@github/webauthn-json', () => ({ get: webauthnGet }));

// Mirrors what App.tsx actually does with LoginScreen: render it while logged out,
// swap to app content once AuthContext reports a session — so a successful login is
// observable without reaching into AuthContext internals.
function Harness() {
  const { username, loading } = useAuth();
  if (loading) return null;
  if (!username) return <LoginScreen />;
  return <p>Bienvenida, {username}</p>;
}

beforeEach(() => {
  webauthnGet.mockReset();
});

describe('LoginScreen', () => {
  it('logs in with email and password', async () => {
    const user = userEvent.setup();
    server.use(
      http.post(`${API_URL}/auth/login`, () => HttpResponse.json(makeLoginResponse({ username: 'alex@example.com' }))),
      http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ username: 'alex@example.com' }))),
    );

    renderWithProviders(<Harness />, { useRealAuthProvider: true });

    await user.type(screen.getByLabelText('Correo electrónico'), 'alex@example.com');
    await user.type(screen.getByLabelText('Contraseña'), 'hunter2');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByText('Bienvenida, alex@example.com')).toBeInTheDocument();
  });

  it('shows the backend error message on a rejected login', async () => {
    const user = userEvent.setup();
    server.use(
      http.post(`${API_URL}/auth/login`, () => HttpResponse.json({ error: 'Credenciales inválidas' }, { status: 401 })),
    );

    renderWithProviders(<Harness />, { useRealAuthProvider: true });

    await user.type(screen.getByLabelText('Correo electrónico'), 'alex@example.com');
    await user.type(screen.getByLabelText('Contraseña'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByText('Credenciales inválidas')).toBeInTheDocument();
  });

  it('completes the two-step passkey login when the account has one registered', async () => {
    const user = userEvent.setup();
    webauthnGet.mockResolvedValue({ id: 'cred-1', rawId: 'cred-1', type: 'public-key', response: {}, clientExtensionResults: {} });
    server.use(
      http.post(`${API_URL}/auth/login`, () =>
        HttpResponse.json(
          makeLoginResponse({ token: null, username: null, mfaRequired: true, sessionId: 'session-1', options: {} }),
        ),
      ),
      http.post(`${API_URL}/auth/login/passkey`, () =>
        HttpResponse.json(makeLoginResponse({ username: 'alex@example.com' })),
      ),
      http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ username: 'alex@example.com' }))),
    );

    renderWithProviders(<Harness />, { useRealAuthProvider: true });

    await user.type(screen.getByLabelText('Correo electrónico'), 'alex@example.com');
    await user.type(screen.getByLabelText('Contraseña'), 'hunter2');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByText('Bienvenida, alex@example.com')).toBeInTheDocument();
    expect(webauthnGet).toHaveBeenCalledTimes(1);
  });
});
