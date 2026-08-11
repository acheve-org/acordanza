import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeFirstAscensionist } from '../test/factories';
import { RequestFirstAscensionistLinkModal } from './RequestFirstAscensionistLinkModal';

describe('RequestFirstAscensionistLinkModal', () => {
  it('fetches the candidate list itself, on open', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () =>
        HttpResponse.json([makeFirstAscensionist({ id: 1, name: 'Alex' })]),
      ),
    );
    renderWithProviders(<RequestFirstAscensionistLinkModal onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByText('Alex')).toBeInTheDocument());
  });

  it('excludes first ascensionists that are already linked to someone', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () =>
        HttpResponse.json([
          makeFirstAscensionist({ id: 1, name: 'Libre' }),
          makeFirstAscensionist({ id: 2, name: 'Vinculado', linkedUserName: 'Otro' }),
        ]),
      ),
    );
    renderWithProviders(<RequestFirstAscensionistLinkModal onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByText('Libre')).toBeInTheDocument());
    expect(screen.queryByText('Vinculado')).not.toBeInTheDocument();
  });

  it('shows the empty state when there are no candidates', async () => {
    server.use(http.get(`${API_URL}/first-ascensionists`, () => HttpResponse.json([])));
    renderWithProviders(<RequestFirstAscensionistLinkModal onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() =>
      expect(screen.getByText('No se han encontrado aperturistas disponibles')).toBeInTheDocument(),
    );
  });

  it('requests a link and calls onRequested', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () =>
        HttpResponse.json([makeFirstAscensionist({ id: 1, name: 'Alex' })]),
      ),
    );
    let body: unknown;
    server.use(
      http.post(`${API_URL}/first-ascensionist-links`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    const onRequested = vi.fn();
    renderWithProviders(<RequestFirstAscensionistLinkModal onClose={vi.fn()} onRequested={onRequested} />);

    await waitFor(() => expect(screen.getByText('Alex')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar' }));

    await waitFor(() => expect(body).toEqual({ firstAscensionistId: 1 }));
    await waitFor(() => expect(onRequested).toHaveBeenCalled());
  });

  it('surfaces a request failure', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () =>
        HttpResponse.json([makeFirstAscensionist({ id: 1, name: 'Alex' })]),
      ),
    );
    server.use(
      http.post(`${API_URL}/first-ascensionist-links`, () =>
        HttpResponse.json({ error: 'Ya tienes una solicitud' }, { status: 400 }),
      ),
    );
    renderWithProviders(<RequestFirstAscensionistLinkModal onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByText('Alex')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar' }));

    await waitFor(() => expect(screen.getByText('Ya tienes una solicitud')).toBeInTheDocument());
  });

  it('surfaces a fetch failure', async () => {
    server.use(http.get(`${API_URL}/first-ascensionists`, () => HttpResponse.error()));
    renderWithProviders(<RequestFirstAscensionistLinkModal onClose={vi.fn()} onRequested={vi.fn()} />);

    await waitFor(() => expect(screen.getByText('No se pudieron cargar los aperturistas')).toBeInTheDocument());
  });
});
