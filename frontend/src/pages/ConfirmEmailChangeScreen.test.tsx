import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { ConfirmEmailChangeScreen } from './ConfirmEmailChangeScreen';

const validEntry = '/confirm-email-change?uid=user-1&email=nuevo%40example.com&token=abc123';

describe('ConfirmEmailChangeScreen', () => {
  it('posts exactly once and renders the success state for a valid link', async () => {
    let requestCount = 0;
    server.use(
      http.post(`${API_URL}/auth/confirm-email-change`, () => {
        requestCount++;
        return HttpResponse.json({ ok: true });
      }),
    );

    renderWithProviders(<ConfirmEmailChangeScreen />, { initialEntries: [validEntry] });

    expect(await screen.findByText('Correo electrónico actualizado. Inicia sesión con tu correo nuevo.')).toBeInTheDocument();
    expect(requestCount).toBe(1);
  });

  it('renders the dead-link message on a rejected confirmation', async () => {
    server.use(
      http.post(`${API_URL}/auth/confirm-email-change`, () =>
        HttpResponse.json({ error: 'El enlace no es válido o ha caducado' }, { status: 400 }),
      ),
    );

    renderWithProviders(<ConfirmEmailChangeScreen />, { initialEntries: [validEntry] });

    expect(await screen.findByText('El enlace no es válido o ha caducado.')).toBeInTheDocument();
  });
});
