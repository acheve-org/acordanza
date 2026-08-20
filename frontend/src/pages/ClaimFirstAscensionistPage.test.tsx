import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeFirstAscensionistLinkRequest, makePagedFirstAscensionists } from '../test/factories';
import { ClaimFirstAscensionistPage } from './ClaimFirstAscensionistPage';

function mockMine(items: ReturnType<typeof makeFirstAscensionistLinkRequest>[]) {
  server.use(http.get(`${API_URL}/first-ascensionist-links/mine`, () => HttpResponse.json(items)));
}

describe('ClaimFirstAscensionistPage', () => {
  it('renders the rows returned by GET /first-ascensionist-links/mine with a status subtitle', async () => {
    mockMine([
      makeFirstAscensionistLinkRequest({ id: 1, firstAscensionistName: 'Alex Aperturista', status: 'pending' }),
    ]);

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() => expect(screen.getByText('Alex Aperturista')).toBeInTheDocument());
    expect(screen.getByText('Pendiente de aprobación')).toBeInTheDocument();
  });

  it('shows Retirar solicitud for a pending row and not for an approved/rejected one', async () => {
    mockMine([
      makeFirstAscensionistLinkRequest({ id: 1, firstAscensionistName: 'Uno', status: 'pending' }),
      makeFirstAscensionistLinkRequest({ id: 2, firstAscensionistName: 'Dos', status: 'approved' }),
      makeFirstAscensionistLinkRequest({ id: 3, firstAscensionistName: 'Tres', status: 'rejected' }),
    ]);

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() =>
      expect(screen.getAllByRole('button', { name: 'Retirar solicitud' })).toHaveLength(1),
    );
  });

  it('the header button is present and opens the request modal', async () => {
    mockMine([]);
    server.use(http.get(`${API_URL}/first-ascensionists`, () => HttpResponse.json(makePagedFirstAscensionists())));

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() => expect(screen.getByText('Todavía no has solicitado ningún vínculo')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Solicitar vínculo' }));

    expect(await screen.findByRole('heading', { name: 'Solicitar vínculo de aperturista' })).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Buscar aperturista…')).toBeInTheDocument();
  });

  it('a failing list request renders .error-text with the fallback message', async () => {
    server.use(http.get(`${API_URL}/first-ascensionist-links/mine`, () => HttpResponse.error()));

    renderWithProviders(<ClaimFirstAscensionistPage />);

    const error = await screen.findByText('No se pudieron cargar tus solicitudes');
    expect(error).toHaveClass('error-text');
  });

  it('the button is enabled with no requests, with the "you may claim someone" intro', async () => {
    mockMine([]);

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Solicitar vínculo' })).not.toBeDisabled(),
    );
    expect(
      screen.getByText(/Al vincularte con un aperturista podrás gestionar sus vías/),
    ).toBeInTheDocument();
  });

  it('the button is enabled when every request is rejected — a rejection does not lock the account out', async () => {
    mockMine([makeFirstAscensionistLinkRequest({ id: 1, firstAscensionistName: 'Alex', status: 'rejected' })]);

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Solicitar vínculo' })).not.toBeDisabled(),
    );
  });

  it('the button is disabled with a pending claim, and the intro names it', async () => {
    mockMine([
      makeFirstAscensionistLinkRequest({ id: 1, firstAscensionistName: 'Alex Aperturista', status: 'pending' }),
    ]);

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Solicitar vínculo' })).toBeDisabled(),
    );
    expect(
      screen.getByText(/Tu solicitud para vincularte con Alex Aperturista está pendiente de aprobación/),
    ).toBeInTheDocument();
  });

  it('the button is disabled with an approved claim, and the intro names it', async () => {
    mockMine([
      makeFirstAscensionistLinkRequest({ id: 1, firstAscensionistName: 'Alex Aperturista', status: 'approved' }),
    ]);

    renderWithProviders(<ClaimFirstAscensionistPage />);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Solicitar vínculo' })).toBeDisabled(),
    );
    expect(screen.getByText(/Estás vinculado con Alex Aperturista/)).toBeInTheDocument();
  });
});
