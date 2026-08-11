import { act, screen, waitFor } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRoutePending } from '../test/factories';
import { Permissions } from '../api/permissions';
import { PendingTasksProvider } from '../context/PendingTasksContext';
import { PendingTasksPage } from './PendingTasksPage';

function LocationProbe({ onLocation }: { onLocation: (pathname: string, hasBackgroundLocation: boolean) => void }) {
  const location = useLocation();
  onLocation(location.pathname, Boolean((location.state as { backgroundLocation?: unknown } | null)?.backgroundLocation));
  return null;
}

describe('PendingTasksPage', () => {
  it('clicking a pending route row navigates to its detail URL with the current location as backgroundLocation', async () => {
    server.use(
      http.get(`${API_URL}/routes/pending`, () =>
        HttpResponse.json([
          makeRoutePending({
            id: 7,
            name: 'Travesía del Gato',
            slug: 'travesia-del-gato',
            areaSlugPath: 'pena-rua',
          }),
        ]),
      ),
    );

    let pathname = '';
    let hasBackgroundLocation = false;
    renderWithProviders(
      <PendingTasksProvider>
        <Routes>
          <Route path="/manage/pending-tasks" element={<PendingTasksPage />} />
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
        </Routes>
      </PendingTasksProvider>,
      {
        initialEntries: ['/manage/pending-tasks'],
        auth: { permissions: [Permissions.routeValidate] },
      },
    );

    const row = await screen.findByText('Travesía del Gato');
    act(() => row.click());

    await waitFor(() => expect(pathname).toBe('/routes/pena-rua/travesia-del-gato'));
    expect(hasBackgroundLocation).toBe(true);
  });
});
