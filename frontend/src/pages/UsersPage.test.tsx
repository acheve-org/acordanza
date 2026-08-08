import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeUserAccount } from '../test/factories';
import { Permissions } from '../api/permissions';
import { UsersPage } from './UsersPage';

function mockUsers(items: ReturnType<typeof makeUserAccount>[]) {
  server.use(
    http.get(`${API_URL}/users`, () =>
      HttpResponse.json({ items, totalCount: items.length, page: 1, pageSize: 10 }),
    ),
  );
}

describe('UsersPage', () => {
  it('shows name/email/role for every account, the pending chip only for the pending one, and invite/confirm meta only where data exists', async () => {
    const confirmed = makeUserAccount({
      id: 'user-confirmed',
      name: 'Ana Ruiz',
      email: 'ana@example.com',
      role: 'moderator',
      pending: false,
      invitedByName: 'Hugo',
      invitedAt: '2026-07-12T10:00:00Z',
      confirmedAt: '2026-07-14T10:00:00Z',
    });
    const pending = makeUserAccount({
      id: 'user-pending',
      name: '',
      email: 'pendiente@example.com',
      role: 'user',
      pending: true,
      invitedByName: 'Hugo',
      invitedAt: '2026-08-01T10:00:00Z',
      confirmedAt: null,
    });
    const cliProvisioned = makeUserAccount({
      id: 'user-cli',
      name: 'Founding Admin',
      email: 'founder@example.com',
      role: 'admin',
      pending: false,
      invitedByName: null,
      invitedAt: null,
      confirmedAt: null,
    });
    mockUsers([confirmed, pending, cliProvisioned]);

    renderWithProviders(<UsersPage />, { auth: { permissions: [Permissions.userManage] } });

    await waitFor(() => expect(screen.getByText('ana@example.com')).toBeInTheDocument());

    const confirmedRow = screen.getByText('ana@example.com').closest('li')!;
    expect(within(confirmedRow).getByText('Ana Ruiz')).toBeInTheDocument();
    expect(within(confirmedRow).getByText('Moderador')).toBeInTheDocument();
    expect(
      within(confirmedRow).getByText('Invitado por Hugo el 12/7/2026 · Confirmado el 14/7/2026'),
    ).toBeInTheDocument();
    expect(within(confirmedRow).queryByText('Sin confirmar')).not.toBeInTheDocument();

    const pendingRow = screen.getByText('pendiente@example.com').closest('li')!;
    expect(within(pendingRow).getByText('Sin confirmar')).toBeInTheDocument();
    expect(within(pendingRow).getByText('Invitado por Hugo el 1/8/2026')).toBeInTheDocument();

    const cliRow = screen.getByText('founder@example.com').closest('li')!;
    expect(within(cliRow).getByText('Founding Admin')).toBeInTheDocument();
    expect(within(cliRow).getByText('Administrador')).toBeInTheDocument();
    expect(within(cliRow).queryByText(/Invitado/)).not.toBeInTheDocument();
    expect(within(cliRow).queryByText(/Confirmado/)).not.toBeInTheDocument();
    expect(within(cliRow).queryByText('Sin confirmar')).not.toBeInTheDocument();
  });
});
