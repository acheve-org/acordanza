import { act, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea, makeAreaExpert, makeFirstAscensionistLinkRequest, makeRoute } from '../test/factories';
import { Permissions } from '../api/permissions';
import { PendingTasksProvider, usePendingTasks } from './PendingTasksContext';

const ALL_PERMISSIONS = [
  Permissions.routeValidate,
  Permissions.areaValidate,
  Permissions.areaExpertApprove,
  Permissions.firstAscensionistLinkApprove,
];

function Probe() {
  const { pendingRoutes, pendingAreas, pendingAreaExperts, pendingFirstAscensionistLinks, total, loading } =
    usePendingTasks();
  return (
    <dl>
      <dt>routes</dt>
      <dd>{pendingRoutes.length}</dd>
      <dt>areas</dt>
      <dd>{pendingAreas.length}</dd>
      <dt>areaExperts</dt>
      <dd>{pendingAreaExperts.length}</dd>
      <dt>faLinks</dt>
      <dd>{pendingFirstAscensionistLinks.length}</dd>
      <dt>total</dt>
      <dd data-testid="total">{total}</dd>
      <dt>loading</dt>
      <dd data-testid="loading">{loading ? 'loading' : 'done'}</dd>
    </dl>
  );
}

function renderProbe(permissions: string[], username: string | null = 'alex') {
  return renderWithProviders(
    <PendingTasksProvider>
      <Probe />
    </PendingTasksProvider>,
    { auth: { username, permissions } },
  );
}

describe('PendingTasksContext', () => {
  it('sums all four categories once every request resolves', async () => {
    server.use(
      http.get(`${API_URL}/routes`, () =>
        HttpResponse.json([
          makeRoute({ id: 1, validatedAt: null, canValidate: true }),
          makeRoute({ id: 2, validatedAt: '2026-01-01T00:00:00Z', canValidate: true }), // already validated
        ]),
      ),
      http.get(`${API_URL}/areas`, () => HttpResponse.json([makeArea({ id: 1, validatedAt: null })])),
      http.get(`${API_URL}/area-experts/pending`, () => HttpResponse.json([makeAreaExpert({ id: 1 })])),
      http.get(`${API_URL}/first-ascensionist-links/pending`, () =>
        HttpResponse.json([makeFirstAscensionistLinkRequest({ id: 1 }), makeFirstAscensionistLinkRequest({ id: 2 })]),
      ),
    );

    renderProbe(ALL_PERMISSIONS);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('done'));
    // 1 pending route + 1 pending area + 1 area expert + 2 fa-links = 5
    expect(screen.getByTestId('total')).toHaveTextContent('5');
  });

  it("one category's failure leaves the other three counts intact, thanks to Promise.allSettled", async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    server.use(
      http.get(`${API_URL}/routes`, () => HttpResponse.json([makeRoute({ id: 1, validatedAt: null, canValidate: true })])),
      http.get(`${API_URL}/areas`, () => HttpResponse.json([makeArea({ id: 1, validatedAt: null })])),
      http.get(`${API_URL}/area-experts/pending`, () => HttpResponse.json({ error: 'fallo' }, { status: 500 })),
      http.get(`${API_URL}/first-ascensionist-links/pending`, () =>
        HttpResponse.json([makeFirstAscensionistLinkRequest({ id: 1 })]),
      ),
    );

    renderProbe(ALL_PERMISSIONS);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('done'));
    // routes(1) + areas(1) + areaExperts(0, failed) + faLinks(1) = 3 — the failure didn't blank the rest.
    expect(screen.getByTestId('total')).toHaveTextContent('3');
    expect(errorSpy).toHaveBeenCalled();

    errorSpy.mockRestore();
  });

  it('fetches nothing when the user has none of the four validate/approve permissions', async () => {
    // No handlers registered for routes/areas/area-experts/first-ascensionist-links — an
    // accidental fetch here would fail the test via onUnhandledRequest: 'error'.
    renderProbe([]);
    await act(async () => {});

    expect(screen.getByTestId('total')).toHaveTextContent('0');
  });
});
