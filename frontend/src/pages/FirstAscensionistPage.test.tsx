import { act, screen, waitFor } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeFirstAscensionistProfile, makeRoute } from '../test/factories';
import { FirstAscensionistPage } from './FirstAscensionistPage';

function LocationProbe({ onLocation }: { onLocation: (pathname: string, hasBackgroundLocation: boolean) => void }) {
  const location = useLocation();
  onLocation(location.pathname, Boolean((location.state as { backgroundLocation?: unknown } | null)?.backgroundLocation));
  return null;
}

describe('FirstAscensionistPage', () => {
  it('renders the aperturista resolved by slug and fetches their routes with the id from the profile', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists/by-slug/hugo-biarge`, () =>
        HttpResponse.json(
          makeFirstAscensionistProfile({ id: 42, name: 'Hugo Biarge', slug: 'hugo-biarge', routeCount: 1 }),
        ),
      ),
      http.get(`${API_URL}/routes`, ({ request }) => {
        expect(new URL(request.url).search).toBe('?firstAscensionistId=42');
        return HttpResponse.json([makeRoute({ id: 7, name: 'Travesía del Gato' })]);
      }),
    );

    renderWithProviders(
      <Routes>
        <Route path="/fa/:slug" element={<FirstAscensionistPage />} />
      </Routes>,
      { initialEntries: ['/fa/hugo-biarge'] },
    );

    expect(await screen.findByRole('heading', { name: 'Hugo Biarge' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('Travesía del Gato')).toBeInTheDocument());
  });

  it('clicking a route card navigates to its detail URL with the current location as backgroundLocation', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists/by-slug/hugo-biarge`, () =>
        HttpResponse.json(makeFirstAscensionistProfile({ id: 42, name: 'Hugo Biarge', slug: 'hugo-biarge' })),
      ),
      http.get(`${API_URL}/routes`, () =>
        HttpResponse.json([
          makeRoute({ id: 7, name: 'Travesía del Gato', slug: 'travesia-del-gato', areaSlugPath: 'pena-rua' }),
        ]),
      ),
    );

    let pathname = '';
    let hasBackgroundLocation = false;
    renderWithProviders(
      <Routes>
        <Route path="/fa/:slug" element={<FirstAscensionistPage />} />
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
      { initialEntries: ['/fa/hugo-biarge'] },
    );

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    await waitFor(() => expect(pathname).toBe('/routes/pena-rua/travesia-del-gato'));
    expect(hasBackgroundLocation).toBe(true);
  });

  it('shows the not-found state for an unknown slug', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists/by-slug/nadie`, () =>
        HttpResponse.json({ error: 'el aperturista no existe' }, { status: 404 }),
      ),
    );

    renderWithProviders(
      <Routes>
        <Route path="/fa/:slug" element={<FirstAscensionistPage />} />
      </Routes>,
      { initialEntries: ['/fa/nadie'] },
    );

    expect(await screen.findByText('Este aperturista no existe.')).toBeInTheDocument();
  });
});
