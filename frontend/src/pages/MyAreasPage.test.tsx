import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeAreaExpert } from '../test/factories';
import { MyAreasPage } from './MyAreasPage';

function mockMine(items: ReturnType<typeof makeAreaExpert>[]) {
  server.use(http.get(`${API_URL}/area-experts/mine`, () => HttpResponse.json(items)));
}

describe('MyAreasPage', () => {
  it('renders the rows returned by GET /area-experts/mine with a status subtitle', async () => {
    mockMine([
      makeAreaExpert({ id: 1, areaName: 'Peña Rúa', status: 'pending' }),
      makeAreaExpert({ id: 2, areaName: 'Sector Norte', status: 'approved', decidedByName: 'Hugo' }),
    ]);

    renderWithProviders(<MyAreasPage />);

    await waitFor(() => expect(screen.getByText('Peña Rúa')).toBeInTheDocument());
    expect(screen.getByText('Pendiente de aprobación')).toBeInTheDocument();
    expect(screen.getByText('Sector Norte')).toBeInTheDocument();
    expect(screen.getByText('Aprobado · Hugo')).toBeInTheDocument();
  });

  it('shows Retirar solicitud for a pending row and not for an approved/rejected one', async () => {
    mockMine([
      makeAreaExpert({ id: 1, areaName: 'Peña Rúa', status: 'pending' }),
      makeAreaExpert({ id: 2, areaName: 'Sector Norte', status: 'approved' }),
      makeAreaExpert({ id: 3, areaName: 'Sector Sur', status: 'rejected' }),
    ]);

    renderWithProviders(<MyAreasPage />);

    await waitFor(() =>
      expect(screen.getAllByRole('button', { name: 'Retirar solicitud' })).toHaveLength(1),
    );
  });

  it('the header button is present and opens the request modal', async () => {
    mockMine([]);
    server.use(http.get(`${API_URL}/areas`, () => HttpResponse.json([])));

    renderWithProviders(<MyAreasPage />);

    await waitFor(() => expect(screen.getByText('Todavía no eres experto de ningún sector')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Solicitar acceso' }));

    expect(await screen.findByRole('heading', { name: 'Solicitar acceso a un sector' })).toBeInTheDocument();
  });

  it('a failing list request renders .error-text with the fallback message', async () => {
    server.use(http.get(`${API_URL}/area-experts/mine`, () => HttpResponse.error()));

    renderWithProviders(<MyAreasPage />);

    const error = await screen.findByText('No se pudieron cargar tus sectores');
    expect(error).toHaveClass('error-text');
  });

  it('refetches the list after withdrawing a pending request', async () => {
    let withdrawn = false;
    server.use(
      http.get(`${API_URL}/area-experts/mine`, () =>
        HttpResponse.json(
          withdrawn ? [] : [makeAreaExpert({ id: 1, areaName: 'Peña Rúa', status: 'pending' })],
        ),
      ),
      http.delete(`${API_URL}/area-experts/1`, () => {
        withdrawn = true;
        return HttpResponse.json({ ok: true });
      }),
    );

    renderWithProviders(<MyAreasPage />);

    await waitFor(() => expect(screen.getByText('Peña Rúa')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Retirar solicitud' }));

    await waitFor(() => expect(screen.getByText('Todavía no eres experto de ningún sector')).toBeInTheDocument());
  });
});
