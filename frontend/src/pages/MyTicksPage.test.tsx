import { act, screen, waitFor } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeTickedRoute } from '../test/factories';
import { MyTicksPage } from './MyTicksPage';

function LocationProbe({ onLocation }: { onLocation: (pathname: string, hasBackgroundLocation: boolean) => void }) {
  const location = useLocation();
  onLocation(location.pathname, Boolean((location.state as { backgroundLocation?: unknown } | null)?.backgroundLocation));
  return null;
}

describe('MyTicksPage', () => {
  // jsdom has no IntersectionObserver (used for the infinite-scroll sentinel) — a
  // no-op stub is enough since this test never scrolls far enough to load more pages.
  beforeAll(() => {
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
  });

  it('fetches the next page when the sentinel intersects', async () => {
    let observedCallback: ((entries: { isIntersecting: boolean }[]) => void) | null = null;
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(cb: (entries: { isIntersecting: boolean }[]) => void) {
          observedCallback = cb;
        }
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );

    const pageOne = Array.from({ length: 20 }, (_, i) =>
      makeTickedRoute({ tickId: i + 1, routeId: i + 1, routeName: `Vía ${i + 1}` }),
    );
    const pageTwo = [makeTickedRoute({ tickId: 21, routeId: 21, routeName: 'Vía 21' })];

    server.use(
      http.get(`${API_URL}/ticks/mine`, ({ request }) => {
        const page = new URL(request.url).searchParams.get('page');
        return HttpResponse.json({
          items: page === '2' ? pageTwo : pageOne,
          totalCount: 21,
          page: page === '2' ? 2 : 1,
          pageSize: 20,
        });
      }),
    );

    renderWithProviders(<MyTicksPage />);

    await screen.findByText('Vía 1');
    expect(screen.queryByText('Vía 21')).not.toBeInTheDocument();

    act(() => observedCallback?.([{ isIntersecting: true }]));

    await waitFor(() => expect(screen.getByText('Vía 21')).toBeInTheDocument());
  });

  it('clicking a ticked route card navigates to its detail URL with the current location as backgroundLocation', async () => {
    server.use(
      http.get(`${API_URL}/ticks/mine`, () =>
        HttpResponse.json({
          items: [
            makeTickedRoute({
              tickId: 3,
              routeId: 7,
              routeName: 'Travesía del Gato',
              routeSlug: 'travesia-del-gato',
              areaSlugPath: 'pena-rua',
            }),
          ],
          totalCount: 1,
          page: 1,
          pageSize: 20,
        }),
      ),
    );

    let pathname = '';
    let hasBackgroundLocation = false;
    renderWithProviders(
      <Routes>
        <Route path="/my-acordanza/my-routes" element={<MyTicksPage />} />
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
      { initialEntries: ['/my-acordanza/my-routes'] },
    );

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    await waitFor(() => expect(pathname).toBe('/routes/pena-rua/travesia-del-gato'));
    expect(hasBackgroundLocation).toBe(true);
  });
});
