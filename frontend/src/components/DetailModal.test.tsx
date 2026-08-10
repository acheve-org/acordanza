import { screen } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRouteDetail } from '../test/factories';
import { Permissions } from '../api/permissions';
import { DetailModal } from './DetailModal';

const noop = () => {};
const noopAsync = async () => {};
const noopRejectAsync = async (_reason: string) => {};

function renderModal(permissions: string[], routeOverrides: Parameters<typeof makeRouteDetail>[0] = {}) {
  return renderWithProviders(
    <DetailModal
      route={makeRouteDetail(routeOverrides)}
      onClose={noop}
      onEdit={noop}
      onDelete={noop}
      onValidate={noopAsync}
      onReject={noopRejectAsync}
      onAddTick={noopAsync}
      onUploadTopo={noopAsync}
      onDeleteTopo={noopAsync}
    />,
    { auth: { permissions } },
  );
}

// Mirrors backend/Acordanza.Api.Tests's authorization matrix (plan 08), but for what the UI
// *shows* rather than what the server *enforces* — the two are independent checks.
describe('DetailModal permission gating', () => {
  it('hides Editar/Eliminar/Registrar ascenso with no permissions', () => {
    renderModal([]);
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Eliminar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Registrar ascenso/ })).not.toBeInTheDocument();
  });

  it('shows only Editar with route.edit', () => {
    renderModal([Permissions.routeEdit]);
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Eliminar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Registrar ascenso/ })).not.toBeInTheDocument();
  });

  it('shows only Eliminar with route.delete', () => {
    renderModal([Permissions.routeDelete]);
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eliminar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Registrar ascenso/ })).not.toBeInTheDocument();
  });

  it('shows only + Registrar ascenso with tick.create', () => {
    renderModal([Permissions.tickCreate]);
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Eliminar' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '+ Registrar ascenso' })).toBeInTheDocument();
  });

  it('shows all three actions with every permission granted', () => {
    renderModal([Permissions.routeEdit, Permissions.routeDelete, Permissions.tickCreate]);
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eliminar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '+ Registrar ascenso' })).toBeInTheDocument();
  });

  it('shows Validar from route.canValidate regardless of permissions — it is server-computed, not a permission', () => {
    renderModal([], { canValidate: true, validationStatus: 'pending' });
    expect(screen.getByRole('button', { name: 'Validar' })).toBeInTheDocument();
  });

  it('hides Validar once the route is already validated, even when canValidate is true', () => {
    renderModal([], {
      canValidate: true,
      validationStatus: 'approved',
      decidedAt: '2026-01-01T00:00:00Z',
      decidedByName: 'Alex',
    });
    expect(screen.queryByRole('button', { name: 'Validar' })).not.toBeInTheDocument();
  });
});

describe('DetailModal new sections (plan 20)', () => {
  it('renders the stats row, gear notes, and links when populated', () => {
    renderModal([], {
      length: 28,
      orientation: 'south',
      gear: 'bolted',
      obligatoryGrade: '6a',
      recommendedGear: 'Friends hasta 3',
      optionalGear: 'Estribos',
      links: ['https://example.com/topo'],
    });

    expect(screen.getByText('28 m')).toBeInTheDocument();
    expect(screen.getByText('Sur')).toBeInTheDocument();
    expect(screen.getByText('Equipada')).toBeInTheDocument();
    expect(screen.getByText('Grado obligatorio: 6a')).toBeInTheDocument();
    expect(screen.getByText('Material recomendado: Friends hasta 3')).toBeInTheDocument();
    expect(screen.getByText('Material opcional: Estribos')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'https://example.com/topo' })).toHaveAttribute(
      'href',
      'https://example.com/topo',
    );
  });

  it('omits the new sections when their fields are empty', () => {
    renderModal([], {
      length: 0,
      orientation: null,
      gear: null,
      obligatoryGrade: null,
      recommendedGear: null,
      optionalGear: null,
      links: [],
    });

    expect(screen.queryByText('Longitud')).not.toBeInTheDocument();
    expect(screen.queryByText('Orientación')).not.toBeInTheDocument();
    expect(screen.queryByText('Material', { selector: '.detail-stat-label' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Grado obligatorio/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Material recomendado/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Material opcional/)).not.toBeInTheDocument();
    expect(screen.queryByText('Enlaces')).not.toBeInTheDocument();
  });
});
