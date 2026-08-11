import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { ChangeEmailModal } from './ChangeEmailModal';

describe('ChangeEmailModal', () => {
  it('moves from the info step to the form on Continuar', () => {
    renderWithProviders(<ChangeEmailModal onClose={vi.fn()} />);
    expect(screen.getByText(/Tu correo electrónico es también tu usuario/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(screen.getByLabelText('Correo electrónico nuevo')).toBeInTheDocument();
  });

  it('submits the new email/password and shows the confirmation step', async () => {
    let body: unknown;
    server.use(
      http.post(`${API_URL}/account/email/change-request`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ok: true });
      }),
    );
    renderWithProviders(<ChangeEmailModal onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Continuar' }));

    fireEvent.change(screen.getByLabelText('Correo electrónico nuevo'), { target: { value: 'nuevo@example.com' } });
    fireEvent.change(screen.getByLabelText('Contraseña actual'), { target: { value: 'secreta123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar enlace' }));

    await waitFor(() => expect(body).toEqual({ newEmail: 'nuevo@example.com', currentPassword: 'secreta123' }));
    await waitFor(() =>
      expect(
        screen.getByText('Te hemos enviado un enlace a nuevo@example.com. Ábrelo para confirmar el cambio.'),
      ).toBeInTheDocument(),
    );
  });

  it('surfaces a failure and stays on the form step', async () => {
    server.use(
      http.post(`${API_URL}/account/email/change-request`, () =>
        HttpResponse.json({ error: 'Contraseña incorrecta' }, { status: 400 }),
      ),
    );
    renderWithProviders(<ChangeEmailModal onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Continuar' }));

    fireEvent.change(screen.getByLabelText('Correo electrónico nuevo'), { target: { value: 'nuevo@example.com' } });
    fireEvent.change(screen.getByLabelText('Contraseña actual'), { target: { value: 'mala' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar enlace' }));

    await waitFor(() => expect(screen.getByText('Contraseña incorrecta')).toBeInTheDocument());
    expect(screen.getByLabelText('Correo electrónico nuevo')).toBeInTheDocument();
  });
});
