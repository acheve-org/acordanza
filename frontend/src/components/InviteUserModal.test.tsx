import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { InviteUserModal } from './InviteUserModal';

describe('InviteUserModal', () => {
  it('submits email/role and calls onInvited on success', async () => {
    let body: unknown;
    server.use(
      http.post(`${API_URL}/users/invite`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ok: true });
      }),
    );
    const onInvited = vi.fn();
    renderWithProviders(<InviteUserModal onClose={vi.fn()} onInvited={onInvited} />);

    fireEvent.change(screen.getByLabelText('Correo electrónico'), { target: { value: 'nuevo@example.com' } });
    fireEvent.change(screen.getByLabelText('Rol'), { target: { value: 'moderator' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar invitación' }));

    await waitFor(() => expect(body).toEqual({ email: 'nuevo@example.com', role: 'moderator' }));
    await waitFor(() => expect(onInvited).toHaveBeenCalled());
  });

  it('surfaces a failure and does not call onInvited', async () => {
    server.use(
      http.post(`${API_URL}/users/invite`, () => HttpResponse.json({ error: 'Cupo agotado' }, { status: 400 })),
    );
    const onInvited = vi.fn();
    renderWithProviders(<InviteUserModal onClose={vi.fn()} onInvited={onInvited} />);

    fireEvent.change(screen.getByLabelText('Correo electrónico'), { target: { value: 'nuevo@example.com' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar invitación' }));

    await waitFor(() => expect(screen.getByText('Cupo agotado')).toBeInTheDocument());
    expect(onInvited).not.toHaveBeenCalled();
  });

  it('shows the role field only when allowRoleSelection is true, and still defaults to "user" when hidden', async () => {
    const { unmount } = renderWithProviders(
      <InviteUserModal onClose={vi.fn()} onInvited={vi.fn()} allowRoleSelection />,
    );
    expect(screen.getByLabelText('Rol')).toBeInTheDocument();
    unmount();

    let body: unknown;
    server.use(
      http.post(`${API_URL}/users/invite`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ok: true });
      }),
    );
    renderWithProviders(<InviteUserModal onClose={vi.fn()} onInvited={vi.fn()} allowRoleSelection={false} />);
    expect(screen.queryByLabelText('Rol')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Correo electrónico'), { target: { value: 'x@example.com' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar invitación' }));

    await waitFor(() => expect(body).toEqual({ email: 'x@example.com', role: 'user' }));
  });
});
