import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { SettingsPage } from './SettingsPage';

function mockNoPasskeys() {
  server.use(http.get(`${API_URL}/passkeys`, () => HttpResponse.json([])));
}

describe('SettingsPage', () => {
  it('shows the account email as a static value, not an editable field', async () => {
    mockNoPasskeys();
    renderWithProviders(<SettingsPage />, { auth: { username: 'alex@example.com' } });

    await waitFor(() => expect(screen.getByText('alex@example.com')).toBeInTheDocument());
    expect(screen.queryByDisplayValue('alex@example.com')).not.toBeInTheDocument();
  });

  it('opens the change-email modal, reveals the form, and shows the sent state on success', async () => {
    mockNoPasskeys();
    server.use(http.post(`${API_URL}/account/email/change-request`, () => HttpResponse.json({ ok: true })));
    const user = userEvent.setup();

    renderWithProviders(<SettingsPage />, { auth: { username: 'alex@example.com' } });

    await user.click(screen.getByRole('button', { name: 'Cambiar correo electrónico' }));
    const dialog = within(screen.getByRole('dialog'));
    expect(dialog.getByText(/Tu correo electrónico es también tu usuario/)).toBeInTheDocument();

    await user.click(dialog.getByRole('button', { name: 'Continuar' }));
    await user.type(dialog.getByLabelText('Correo electrónico nuevo'), 'nuevo@example.com');
    await user.type(dialog.getByLabelText('Contraseña actual'), 'Test1234!');
    await user.click(dialog.getByRole('button', { name: 'Enviar enlace' }));

    expect(await dialog.findByText(/Te hemos enviado un enlace a nuevo@example.com/)).toBeInTheDocument();
  });

  it('surfaces a backend error in the modal form', async () => {
    mockNoPasskeys();
    server.use(
      http.post(`${API_URL}/account/email/change-request`, () =>
        HttpResponse.json({ error: 'Contraseña incorrecta' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();

    renderWithProviders(<SettingsPage />, { auth: { username: 'alex@example.com' } });

    await user.click(screen.getByRole('button', { name: 'Cambiar correo electrónico' }));
    const dialog = within(screen.getByRole('dialog'));
    await user.click(dialog.getByRole('button', { name: 'Continuar' }));
    await user.type(dialog.getByLabelText('Correo electrónico nuevo'), 'nuevo@example.com');
    await user.type(dialog.getByLabelText('Contraseña actual'), 'wrong');
    await user.click(dialog.getByRole('button', { name: 'Enviar enlace' }));

    expect(await dialog.findByText('Contraseña incorrecta')).toBeInTheDocument();
  });
});
