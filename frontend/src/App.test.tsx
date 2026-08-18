import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { http, HttpResponse } from 'msw';
import { server } from './test/server';
import { API_URL } from './test/apiUrl';
import { createTestQueryClient, makeAuthValue } from './test/renderWithProviders';
import { AuthContext } from './context/auth-context';
import { PendingTasksProvider } from './context/PendingTasksContext';
import { makeRoute, makeRouteDetail } from './test/factories';
import App from './App';

// The background-location mechanic (plan 21) is the riskiest new piece here — until now
// covered only by manual click-through — so this exercises the real App tree (not just
// SearchPage/RouteDetailOverlay in isolation): a real BrowserRouter (so window.history
// actually drives it, letting the browser-back assertion below use the real API) plus
// PendingTasksProvider, since AppLayout renders the pending-tasks bell.
function renderApp(initialPath: string) {
  window.history.pushState(null, '', initialPath);
  const client = createTestQueryClient();
  return render(
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <AuthContext.Provider value={makeAuthValue()}>
          <PendingTasksProvider>
            <App />
          </PendingTasksProvider>
        </AuthContext.Provider>
      </BrowserRouter>
    </QueryClientProvider>,
  );
}

function mockRouteList() {
  server.use(
    http.get(`${API_URL}/routes/grades`, () => HttpResponse.json([])),
    http.get(`${API_URL}/routes/aid-grades`, () => HttpResponse.json([])),
    http.get(`${API_URL}/routes`, () =>
      HttpResponse.json([
        makeRoute({ id: 7, name: 'Travesía del Gato', slug: 'travesia-del-gato', areaSlugPath: 'pena-rua' }),
      ]),
    ),
  );
}

function mockRouteDetail() {
  server.use(
    http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
      HttpResponse.json(makeRouteDetail({ id: 7, name: 'Travesía del Gato' })),
    ),
  );
}

describe('App background-location overlay', () => {
  it('opening a route keeps the underlying list mounted (its local state survives) and closes back to it', async () => {
    mockRouteList();
    mockRouteDetail();

    renderApp('/routes');

    const searchInput = await screen.findByPlaceholderText('Nombre de la vía o descripción…');
    act(() => {
      fireEvent.change(searchInput, { target: { value: 'gat' } });
    });
    await waitFor(() => expect((searchInput as HTMLInputElement).value).toBe('gat'));

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    // The list behind the overlay is still mounted, not unmounted/remounted — the typed
    // filter text (component-local state, wiped by a remount) is still there.
    expect((screen.getByPlaceholderText('Nombre de la vía o descripción…') as HTMLInputElement).value).toBe('gat');
    expect(window.location.pathname).toBe('/routes/pena-rua/travesia-del-gato');

    // Browser back closes the overlay and returns to exactly the prior location/state.
    act(() => {
      window.history.back();
    });

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(window.location.pathname).toBe('/routes');
    expect((screen.getByPlaceholderText('Nombre de la vía o descripción…') as HTMLInputElement).value).toBe('gat');
  });

  it('a direct render at a route detail URL with no prior state falls back to SearchPage with the overlay open', async () => {
    mockRouteList();
    mockRouteDetail();

    renderApp('/routes/pena-rua/travesia-del-gato');

    // SearchPage (the fallback background page) renders underneath...
    expect(await screen.findByPlaceholderText('Nombre de la vía o descripción…')).toBeInTheDocument();
    // ...and the overlay opens on top, rather than a blank/broken route match.
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Travesía del Gato' })).toBeInTheDocument();
  });
});
