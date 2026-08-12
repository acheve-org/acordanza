import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRouteDetail } from '../test/factories';
import { ApiError } from '../api/client';
import { Permissions } from '../api/permissions';
import { DetailModal } from './DetailModal';

const noop = () => {};
const noopAsync = async () => {};
const noopRejectAsync = async (_reason: string) => {};

function renderModal(
  permissions: string[],
  routeOverrides: Parameters<typeof makeRouteDetail>[0] = {},
  onEnrich?: (force: boolean) => Promise<void>,
) {
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
      onEnrich={onEnrich}
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
    expect(screen.getByText('✓ Validada por Alex el 1/1/2026')).toBeInTheDocument();
  });

  it('shows the rejection line with the es-ES decision date', () => {
    renderModal([], {
      validationStatus: 'rejected',
      decidedAt: '2026-01-01T00:00:00Z',
      decidedByName: 'Alex',
    });
    expect(screen.getByText('✗ Rechazada por Alex el 1/1/2026')).toBeInTheDocument();
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
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument();
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

// Plan 18 (route-enrichment-worker), issue #169.
describe('DetailModal route enrichment', () => {
  it('hides the button without route.enrich even when onEnrich is wired up', () => {
    renderModal([], {}, noopAsync);
    expect(screen.queryByRole('button', { name: /Buscar enlaces y topo/ })).not.toBeInTheDocument();
  });

  it('hides the button for a user who holds route.edit but not route.enrich', () => {
    renderModal([Permissions.routeEdit], {}, noopAsync);
    expect(screen.queryByRole('button', { name: /Buscar enlaces y topo/ })).not.toBeInTheDocument();
  });

  it('hides the button when the page has not wired up onEnrich, even with the permission', () => {
    renderModal([Permissions.routeEnrich]);
    expect(screen.queryByRole('button', { name: /Buscar enlaces y topo/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Volver a buscar/ })).not.toBeInTheDocument();
  });

  it('shows "Buscar enlaces y topo" when enrichedAt is null', () => {
    renderModal([Permissions.routeEnrich], { enrichedAt: null }, noopAsync);
    expect(screen.getByRole('button', { name: 'Buscar enlaces y topo' })).toBeInTheDocument();
  });

  it('shows "Volver a buscar" plus the last-search date once enrichedAt is set', () => {
    renderModal([Permissions.routeEnrich], { enrichedAt: '2026-01-01T00:00:00Z' }, noopAsync);
    expect(screen.getByRole('button', { name: 'Volver a buscar' })).toBeInTheDocument();
    expect(screen.getByText('Última búsqueda: 1/1/2026')).toBeInTheDocument();
  });

  it('posts once, disables the button, and shows the inline status note on click', async () => {
    const user = userEvent.setup();
    const onEnrich = vi.fn().mockResolvedValue(undefined);
    renderModal([Permissions.routeEnrich], { enrichedAt: null }, onEnrich);

    await user.click(screen.getByRole('button', { name: 'Buscar enlaces y topo' }));

    expect(onEnrich).toHaveBeenCalledTimes(1);
    expect(onEnrich).toHaveBeenCalledWith(false);
    await waitFor(() =>
      expect(
        screen.getByText(/Buscando en la web\. Los resultados aparecerán en unos minutos/),
      ).toBeInTheDocument(),
    );
  });

  it('re-running an already-enriched route asks for confirmation and sends force=true', async () => {
    const user = userEvent.setup();
    const onEnrich = vi.fn().mockResolvedValue(undefined);
    renderModal([Permissions.routeEnrich], { enrichedAt: '2026-01-01T00:00:00Z' }, onEnrich);

    await user.click(screen.getByRole('button', { name: 'Volver a buscar' }));
    const confirmDialog = screen.getAllByRole('dialog').at(-1)!;
    await user.click(within(confirmDialog).getByRole('button', { name: 'Volver a buscar' }));

    await waitFor(() => expect(onEnrich).toHaveBeenCalledWith(true));
  });

  it('surfaces a 409 in the inline error slot', async () => {
    const user = userEvent.setup();
    const onEnrich = vi.fn().mockRejectedValue(new ApiError(409, 'Esta vía ya se ha enriquecido'));
    renderModal([Permissions.routeEnrich], { enrichedAt: null }, onEnrich);

    await user.click(screen.getByRole('button', { name: 'Buscar enlaces y topo' }));

    await waitFor(() => expect(screen.getByText('Esta vía ya se ha enriquecido')).toBeInTheDocument());
  });

  it('surfaces a 503 in the inline error slot', async () => {
    const user = userEvent.setup();
    const onEnrich = vi.fn().mockRejectedValue(new ApiError(503, 'No se pudo iniciar la búsqueda, inténtalo de nuevo'));
    renderModal([Permissions.routeEnrich], { enrichedAt: null }, onEnrich);

    await user.click(screen.getByRole('button', { name: 'Buscar enlaces y topo' }));

    await waitFor(() =>
      expect(screen.getByText('No se pudo iniciar la búsqueda, inténtalo de nuevo')).toBeInTheDocument(),
    );
  });
});
