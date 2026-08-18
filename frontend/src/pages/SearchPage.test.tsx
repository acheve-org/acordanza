import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRoute } from '../test/factories';
import { SearchPage } from './SearchPage';

function LocationProbe({ onLocation }: { onLocation: (pathname: string, hasBackgroundLocation: boolean) => void }) {
  const location = useLocation();
  onLocation(location.pathname, Boolean((location.state as { backgroundLocation?: unknown } | null)?.backgroundLocation));
  return null;
}

function mockGrades(grades: string[] = []) {
  server.use(http.get(`${API_URL}/routes/grades`, () => HttpResponse.json(grades)));
  server.use(http.get(`${API_URL}/routes/aid-grades`, () => HttpResponse.json([])));
}

describe('SearchPage', () => {
  it('debounces filter typing into a single request 250ms after the last keystroke', async () => {
    vi.useFakeTimers();
    try {
      mockGrades();
      const queries: string[] = [];
      server.use(
        http.get(`${API_URL}/routes`, ({ request }) => {
          queries.push(new URL(request.url).search);
          return HttpResponse.json([]);
        }),
      );

      renderWithProviders(<SearchPage />);
      await act(async () => {});
      expect(queries).toEqual(['']); // the initial, undebounced load

      const input = screen.getByPlaceholderText('Nombre de la vía o descripción…');
      for (const value of ['t', 'tr', 'tra']) {
        act(() => {
          fireEvent.change(input, { target: { value } });
        });
        act(() => {
          vi.advanceTimersByTime(100); // < 250ms: debounce keeps resetting
        });
      }
      expect(queries).toHaveLength(1); // still just the initial load

      act(() => {
        vi.advanceTimersByTime(250);
      });
      vi.useRealTimers();

      await waitFor(() => expect(queries).toHaveLength(2));
      expect(queries[1]).toBe('?q=tra');
    } finally {
      vi.useRealTimers();
    }
  });

  it('sends each changed filter as its own query param', async () => {
    vi.useFakeTimers();
    try {
      mockGrades(['6a', '6b']);
      const queries: string[] = [];
      server.use(
        http.get(`${API_URL}/routes`, ({ request }) => {
          queries.push(new URL(request.url).search);
          return HttpResponse.json([]);
        }),
      );

      renderWithProviders(<SearchPage />);
      await act(async () => {});
      expect(queries).toEqual(['']);

      // Grado's <select> immediately follows its <label class="field-label">Grado</label> —
      // Filters uses plain <label>/<select> pairs, not htmlFor/id, so match by DOM position.
      const gradoLabel = screen.getByText('Grado');
      const select = gradoLabel.nextElementSibling as HTMLSelectElement;
      act(() => {
        fireEvent.change(select, { target: { value: '6a' } });
      });
      act(() => {
        vi.advanceTimersByTime(250);
      });
      vi.useRealTimers();

      await waitFor(() => expect(queries).toHaveLength(2));
      expect(queries[1]).toBe('?grade=6a');
    } finally {
      vi.useRealTimers();
    }
  });

  it('clicking a route card navigates to its detail URL, carrying the current location as backgroundLocation', async () => {
    mockGrades();
    const route = makeRoute({ id: 7, name: 'Travesía del Gato', slug: 'travesia-del-gato', areaSlugPath: 'pena-rua' });
    server.use(http.get(`${API_URL}/routes`, () => HttpResponse.json([route])));

    let pathname = '';
    let hasBackgroundLocation = false;
    renderWithProviders(
      <Routes>
        <Route
          path="/routes"
          element={<SearchPage />}
        />
        <Route
          path="*"
          element={
            <LocationProbe
              onLocation={(p, hasBg) => {
                pathname = p;
                hasBackgroundLocation = hasBg;
              }}
            />
          }
        />
      </Routes>,
      { initialEntries: ['/routes'] },
    );

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    await waitFor(() => expect(pathname).toBe('/routes/pena-rua/travesia-del-gato'));
    expect(hasBackgroundLocation).toBe(true);
  });

  it('a fresh visit to a route detail URL synthesizes backgroundLocation state pointing at /routes', async () => {
    mockGrades();
    server.use(http.get(`${API_URL}/routes`, () => HttpResponse.json([])));

    let pathname = '';
    let backgroundLocation: { pathname?: string } | undefined;
    function StateProbe() {
      const location = useLocation();
      pathname = location.pathname;
      backgroundLocation = (location.state as { backgroundLocation?: { pathname?: string } } | null)
        ?.backgroundLocation;
      return <SearchPage />;
    }

    renderWithProviders(
      <Routes>
        <Route path="routes/*" element={<StateProbe />} />
      </Routes>,
      { initialEntries: ['/routes/pena-rua/travesia-del-gato'] },
    );

    await waitFor(() => expect(backgroundLocation).toEqual({ pathname: '/routes' }));
    expect(pathname).toBe('/routes/pena-rua/travesia-del-gato');
  });

  it('renders the backend Spanish error message when the list fails to load', async () => {
    mockGrades();
    server.use(
      http.get(`${API_URL}/routes`, () =>
        HttpResponse.json({ error: 'No se pudieron cargar las vías' }, { status: 500 }),
      ),
    );

    renderWithProviders(<SearchPage />);

    expect(await screen.findByText('No se pudieron cargar las vías')).toBeInTheDocument();
  });
});
