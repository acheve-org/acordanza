import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRouteDetail, makeTopo } from '../test/factories';
import { ApiError } from '../api/client';
import { Permissions } from '../api/permissions';
import { RouteDetailModal } from './RouteDetailModal';

const noop = () => {};
const noopAsync = async () => {};
const noopRejectAsync = async (_reason: string) => {};

function renderModal(
  permissions: string[],
  routeOverrides: Parameters<typeof makeRouteDetail>[0] = {},
  onEnrich?: (force: boolean) => Promise<void>,
) {
  return renderWithProviders(
    <RouteDetailModal
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
  });

  // The modal itself no longer reports the verdict — who decided, when, and why lives on
  // /my-acordanza/contributions. Only the review *actions* are still here, and only while
  // the decision is outstanding.
  it('shows no verdict line for a decided route', () => {
    renderModal([], {
      validationStatus: 'rejected',
      decidedAt: '2026-01-01T00:00:00Z',
      decidedByName: 'Alex',
      rejectionReason: 'Falta el croquis',
    });
    expect(screen.queryByText(/Rechazada por/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Falta el croquis/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument();
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
      links: [
        { url: 'https://example.com/topo', source: 'human' },
        { url: 'https://example.com/auto', source: 'enrichment' },
      ],
    });

    // Grade and length are the head's second identity pair now, not stats-row entries;
    // the obligatory grade rides along inside the GradePill.
    expect(screen.getByText('28 m')).toBeInTheDocument();
    expect(screen.getByText('6a (6a)')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument();
    expect(screen.getByText('Sur')).toBeInTheDocument();
    expect(screen.getByText('Equipada')).toBeInTheDocument();
    expect(screen.getByText('Material recomendado: Friends hasta 3')).toBeInTheDocument();
    expect(screen.getByText('Material opcional: Estribos')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'https://example.com/topo' })).toHaveAttribute(
      'href',
      'https://example.com/topo',
    );
    // Only the worker-appended one is badged (plan 29 §6).
    expect(screen.getByRole('link', { name: /https:\/\/example\.com\/auto/ })).toHaveTextContent(
      'automático',
    );
    expect(screen.getByRole('link', { name: 'https://example.com/topo' })).not.toHaveTextContent(
      'automático',
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

// The two-column split: the left column is what there is to read, the rail is what there
// is to do. Pinning it here is what stops a later action from being dropped back into a
// content section, which is exactly how the pre-split layout drifted.
describe('DetailModal two-column layout', () => {
  const actionsRail = () => screen.getByRole('complementary', { name: 'Acciones' });

  it('puts every action in the rail, none in the content column', () => {
    renderModal(
      [Permissions.routeEdit, Permissions.routeDelete, Permissions.tickCreate, Permissions.routeEnrich],
      { canValidate: true, validationStatus: 'pending' },
      noopAsync,
    );

    const rail = actionsRail();
    for (const name of ['Validar', 'Rechazar', 'Editar', 'Eliminar', '+ Registrar ascenso', 'Buscar enlaces y topo']) {
      expect(within(rail).getByRole('button', { name })).toBeInTheDocument();
    }
    // The upload is a <label> wrapping a file input, so it has no button role.
    expect(within(rail).getByText('+ Añadir topo')).toBeInTheDocument();
    // Nothing actionable is left outside the rail — the modal's only other buttons are
    // in the stacked confirm dialogs, which aren't open here.
    expect(screen.getAllByRole('button')).toHaveLength(within(rail).getAllByRole('button').length);
  });

  it('offers the topo upload but no Topos section when the route has no photos', () => {
    renderModal([], { topos: [] });
    expect(within(actionsRail()).getByText('+ Añadir topo')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Topos' })).not.toBeInTheDocument();
  });

  it('renders the Topos section in the content column once there are photos', () => {
    renderModal([], { topos: [makeTopo()] });
    expect(screen.getByRole('heading', { name: 'Topos' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'topo.jpg' })).toBeInTheDocument();
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
