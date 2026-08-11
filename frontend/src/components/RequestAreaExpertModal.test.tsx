import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea } from '../test/factories';
import { RequestAreaExpertModal } from './RequestAreaExpertModal';

function mockAreas(areas: ReturnType<typeof makeArea>[]) {
  server.use(http.get(`${API_URL}/areas`, () => HttpResponse.json(areas)));
}

describe('RequestAreaExpertModal', () => {
  it('fetches the area list itself, on open', async () => {
    mockAreas([makeArea({ id: 1, name: 'Peña Rúa', parentAreaId: null })]);
    renderWithProviders(<RequestAreaExpertModal excludedAreaIds={[]} onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'Peña Rúa' })).toBeInTheDocument());
  });

  it('excludes areas already requested and filters out non-root areas', async () => {
    mockAreas([
      makeArea({ id: 1, name: 'Peña Rúa', parentAreaId: null }),
      makeArea({ id: 2, name: 'Ya solicitada', parentAreaId: null }),
      makeArea({ id: 3, name: 'Subsector', parentAreaId: 1 }),
    ]);
    renderWithProviders(<RequestAreaExpertModal excludedAreaIds={[2]} onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'Peña Rúa' })).toBeInTheDocument());
    expect(screen.queryByRole('option', { name: 'Ya solicitada' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Subsector' })).not.toBeInTheDocument();
  });

  it('shows "No quedan sectores disponibles" when nothing is left to request', async () => {
    mockAreas([]);
    renderWithProviders(<RequestAreaExpertModal excludedAreaIds={[]} onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByText('No quedan sectores disponibles')).toBeInTheDocument());
  });

  it('submits the selected area and calls onRequested', async () => {
    mockAreas([makeArea({ id: 1, name: 'Peña Rúa', parentAreaId: null })]);
    let body: unknown;
    server.use(
      http.post(`${API_URL}/area-experts`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    const onRequested = vi.fn();
    renderWithProviders(<RequestAreaExpertModal excludedAreaIds={[]} onClose={vi.fn()} onRequested={onRequested} />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'Peña Rúa' })).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Sector'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar' }));

    await waitFor(() => expect(body).toEqual({ areaId: 1 }));
    await waitFor(() => expect(onRequested).toHaveBeenCalled());
  });

  it('surfaces a submit failure', async () => {
    mockAreas([makeArea({ id: 1, name: 'Peña Rúa', parentAreaId: null })]);
    server.use(
      http.post(`${API_URL}/area-experts`, () => HttpResponse.json({ error: 'Ya tienes acceso' }, { status: 400 })),
    );
    renderWithProviders(<RequestAreaExpertModal excludedAreaIds={[]} onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByRole('option', { name: 'Peña Rúa' })).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Sector'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar' }));

    await waitFor(() => expect(screen.getByText('Ya tienes acceso')).toBeInTheDocument());
  });

  it('surfaces a fetch failure', async () => {
    server.use(http.get(`${API_URL}/areas`, () => HttpResponse.error()));
    renderWithProviders(<RequestAreaExpertModal excludedAreaIds={[]} onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByText('No se pudieron cargar los sectores')).toBeInTheDocument());
  });
});
