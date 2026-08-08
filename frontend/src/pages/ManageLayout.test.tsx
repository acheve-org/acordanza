import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { Permissions } from '../api/permissions';
import { ManageLayout } from './ManageLayout';

const { usePendingTasksMock } = vi.hoisted(() => ({ usePendingTasksMock: vi.fn() }));
vi.mock('../context/PendingTasksContext', () => ({ usePendingTasks: usePendingTasksMock }));

function mockPendingTasks(visible: boolean, total = 0) {
  usePendingTasksMock.mockReturnValue({ visible, total });
}

describe('ManageLayout permission gating', () => {
  it('redirects away (renders no nav) when the user has no manage permissions and nothing pending', () => {
    mockPendingTasks(false);
    renderWithProviders(<ManageLayout />, { auth: { permissions: [] } });
    expect(screen.queryByText('Gestionar')).not.toBeInTheDocument();
  });

  it('shows only Usuarios with user.manage', () => {
    mockPendingTasks(false);
    renderWithProviders(<ManageLayout />, { auth: { permissions: [Permissions.userManage] } });
    expect(screen.getByRole('link', { name: 'Usuarios' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Sectores' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Aperturistas' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Tareas pendientes/ })).not.toBeInTheDocument();
  });

  it('shows only Sectores with area.edit', () => {
    mockPendingTasks(false);
    renderWithProviders(<ManageLayout />, { auth: { permissions: [Permissions.areaEdit] } });
    expect(screen.queryByRole('link', { name: 'Usuarios' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sectores' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Aperturistas' })).not.toBeInTheDocument();
  });

  it('shows only Aperturistas with first-ascensionist.delete', () => {
    mockPendingTasks(false);
    renderWithProviders(<ManageLayout />, { auth: { permissions: [Permissions.firstAscensionistDelete] } });
    expect(screen.queryByRole('link', { name: 'Usuarios' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Sectores' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Aperturistas' })).toBeInTheDocument();
  });

  it('shows Tareas pendientes purely from pending-tasks visibility, independent of manage permissions', () => {
    mockPendingTasks(true, 3);
    renderWithProviders(<ManageLayout />, { auth: { permissions: [] } });
    expect(screen.getByRole('link', { name: /Tareas pendientes/ })).toBeInTheDocument();
  });

  it('shows all sections when every permission is granted', () => {
    mockPendingTasks(true, 1);
    renderWithProviders(<ManageLayout />, {
      auth: {
        permissions: [Permissions.userManage, Permissions.areaEdit, Permissions.firstAscensionistDelete],
      },
    });
    expect(screen.getByRole('link', { name: 'Usuarios' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sectores' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Aperturistas' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Tareas pendientes/ })).toBeInTheDocument();
  });
});
