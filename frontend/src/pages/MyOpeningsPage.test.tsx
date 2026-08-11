import { act, screen, waitFor } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRoute } from '../test/factories';
import { MyOpeningsPage } from './MyOpeningsPage';

function LocationProbe({ onLocation }: { onLocation: (pathname: string, hasBackgroundLocation: boolean) => void }) {
  const location = useLocation();
  onLocation(location.pathname, Boolean((location.state as { backgroundLocation?: unknown } | null)?.backgroundLocation));
  return null;
}

describe('MyOpeningsPage', () => {
  it('clicking a route card navigates to its detail URL with the current location as backgroundLocation', async () => {
    server.use(
      http.get(`${API_URL}/routes`, ({ request }) => {
        expect(new URL(request.url).search).toBe('?openedByMe=true');
        return HttpResponse.json([
          makeRoute({ id: 7, name: 'Travesía del Gato', slug: 'travesia-del-gato', areaSlugPath: 'pena-rua' }),
        ]);
      }),
    );

    let pathname = '';
    let hasBackgroundLocation = false;
    renderWithProviders(
      <Routes>
        <Route path="/my-acordanza/my-openings" element={<MyOpeningsPage />} />
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
      { initialEntries: ['/my-acordanza/my-openings'], auth: { isFirstAscensionist: true } },
    );

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    await waitFor(() => expect(pathname).toBe('/routes/pena-rua/travesia-del-gato'));
    expect(hasBackgroundLocation).toBe(true);
  });
});
