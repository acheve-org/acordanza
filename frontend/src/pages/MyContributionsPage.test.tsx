import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeContribution } from '../test/factories';
import { MyContributionsPage } from './MyContributionsPage';

type Contribution = ReturnType<typeof makeContribution>;

// Registers a handler for one kind only — the point of most of these tests is which
// endpoints the page does *not* call (setup.ts's onUnhandledRequest: 'error').
function mockKind(kind: string, items: Contribution[], totalCount = items.length) {
  server.use(
    http.get(`${API_URL}/contributions/${kind}`, ({ request }) => {
      const page = Number(new URL(request.url).searchParams.get('page') ?? 1);
      const pageSize = Number(new URL(request.url).searchParams.get('pageSize') ?? 10);
      return HttpResponse.json({
        items: items.slice((page - 1) * pageSize, page * pageSize),
        totalCount,
        page,
        pageSize,
      });
    }),
  );
}

describe('MyContributionsPage', () => {
  it('renders the routes tab with name, status chip and rejection reason', async () => {
    mockKind('routes', [
      makeContribution({ id: 1, name: 'Diedro del Sol', detail: 'Peña Rúa › Sector 1' }),
      makeContribution({
        id: 2,
        name: 'Fisura Negra',
        detail: 'Peña Rúa › Sector 2',
        validationStatus: 'rejected',
        rejectionReason: 'Ya existe',
      }),
    ]);

    renderWithProviders(<MyContributionsPage />);

    expect(await screen.findByText('Diedro del Sol')).toBeInTheDocument();
    expect(screen.getByText('Peña Rúa › Sector 1')).toBeInTheDocument();
    expect(screen.getByText('Pendiente de validación')).toBeInTheDocument();
    expect(screen.getByText('Creado el 1/1/2026')).toBeInTheDocument();
    expect(screen.getByText('Fisura Negra')).toBeInTheDocument();
    expect(screen.getByText('Rechazado')).toBeInTheDocument();
    expect(screen.getByText(/Motivo: Ya existe/)).toBeInTheDocument();
  });

  it('fetches only the active tab, and the next one only once it is opened', async () => {
    mockKind('routes', [makeContribution({ id: 1, name: 'Diedro del Sol' })]);

    renderWithProviders(<MyContributionsPage />);

    // Nothing but /contributions/routes is handled at this point, so any request the page
    // made for areas or first-ascensionists would fail the test outright.
    expect(await screen.findByText('Diedro del Sol')).toBeInTheDocument();

    mockKind('areas', [makeContribution({ id: 9, name: 'Sector Norte', detail: 'Peña Rúa › Sector Norte' })]);
    fireEvent.click(screen.getByRole('button', { name: 'Sectores' }));

    expect(await screen.findByText('Sector Norte')).toBeInTheDocument();
  });

  it('keeps each tab on its own page when switching back and forth', async () => {
    const routes = Array.from({ length: 15 }, (_, i) =>
      makeContribution({ id: i + 1, name: `Vía ${i + 1}` }),
    );
    mockKind('routes', routes);
    mockKind('areas', [makeContribution({ id: 100, name: 'Sector Norte' })]);

    renderWithProviders(<MyContributionsPage />);

    expect(await screen.findByText('Vía 1')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(await screen.findByText('Vía 11')).toBeInTheDocument();
    expect(screen.getByText('Página 2 de 2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Sectores' }));
    expect(await screen.findByText('Sector Norte')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Vías' }));
    expect(await screen.findByText('Vía 11')).toBeInTheDocument();
    expect(screen.getByText('Página 2 de 2')).toBeInTheDocument();
  });

  it('renders no pagination when everything fits on one page', async () => {
    mockKind('routes', [makeContribution({ id: 1, name: 'Diedro del Sol' })]);

    renderWithProviders(<MyContributionsPage />);

    expect(await screen.findByText('Diedro del Sol')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Siguiente' })).not.toBeInTheDocument();
  });

  it('renders no count badge on any tab', async () => {
    mockKind('routes', [makeContribution({ id: 1, name: 'Diedro del Sol' })]);

    const { container } = renderWithProviders(<MyContributionsPage />);

    expect(await screen.findByText('Diedro del Sol')).toBeInTheDocument();
    expect(container.querySelectorAll('.badge')).toHaveLength(0);
    for (const label of ['Vías', 'Sectores', 'Aperturistas']) {
      expect(screen.getByRole('button', { name: label })).toHaveTextContent(new RegExp(`^${label}$`));
    }
  });

  it('shows the fallback error message when the active tab fails to load', async () => {
    server.use(http.get(`${API_URL}/contributions/routes`, () => HttpResponse.error()));

    renderWithProviders(<MyContributionsPage />);

    await waitFor(() =>
      expect(screen.getByText('No se pudieron cargar tus contribuciones')).toBeInTheDocument(),
    );
  });
});
