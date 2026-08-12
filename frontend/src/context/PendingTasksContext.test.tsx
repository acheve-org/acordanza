import { act, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeAreaExpert, makeAreaPending, makeFirstAscensionistLinkRequest, makeRoutePending } from '../test/factories';
import { Permissions } from '../api/permissions';
import { PendingTasksProvider } from './PendingTasksContext';
import { usePendingTasks } from '../hooks/usePendingTasks';

const ALL_PERMISSIONS = [
  Permissions.routeValidate,
  Permissions.areaValidate,
  Permissions.areaExpertApprove,
  Permissions.firstAscensionistLinkApprove,
  Permissions.firstAscensionistValidate,
];

function Probe() {
  const {
    pendingRoutes,
    pendingAreas,
    pendingAreaExperts,
    pendingFirstAscensionistLinks,
    pendingFirstAscensionists,
    total,
    loading,
  } = usePendingTasks();
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
      <dt>firstAscensionists</dt>
      <dd>{pendingFirstAscensionists.length}</dd>
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
  it('sums all five categories once every request resolves', async () => {
    // GET /routes and GET /areas (the pre-plan-11 endpoints this context used to filter
    // client-side) deliberately have no handler here — onUnhandledRequest: 'error' fails
    // the test if the context regresses to fetching the whole table again instead of the
    // narrowed /routes/pending and /areas/pending endpoints.
    server.use(
      http.get(`${API_URL}/routes/pending`, () => HttpResponse.json([makeRoutePending({ id: 1 })])),
      http.get(`${API_URL}/areas/pending`, () => HttpResponse.json([makeAreaPending({ id: 1 })])),
      http.get(`${API_URL}/area-experts/pending`, () => HttpResponse.json([makeAreaExpert({ id: 1 })])),
      http.get(`${API_URL}/first-ascensionist-links/pending`, () =>
        HttpResponse.json([makeFirstAscensionistLinkRequest({ id: 1 }), makeFirstAscensionistLinkRequest({ id: 2 })]),
      ),
      http.get(`${API_URL}/first-ascensionists/pending`, () =>
        HttpResponse.json([{ id: 1, name: 'Alex Aperturista', createdAt: '2026-01-01T00:00:00Z', addedByName: 'Alex' }]),
      ),
    );

    renderProbe(ALL_PERMISSIONS);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('done'));
    // 1 pending route + 1 pending area + 1 area expert + 2 fa-links + 1 first ascensionist = 6
    expect(screen.getByTestId('total')).toHaveTextContent('6');
  });

  it("one category's failure leaves the other four counts intact, since each category is its own query", async () => {
    server.use(
      http.get(`${API_URL}/routes/pending`, () => HttpResponse.json([makeRoutePending({ id: 1 })])),
      http.get(`${API_URL}/areas/pending`, () => HttpResponse.json([makeAreaPending({ id: 1 })])),
      http.get(`${API_URL}/area-experts/pending`, () => HttpResponse.json({ error: 'fallo' }, { status: 500 })),
      http.get(`${API_URL}/first-ascensionist-links/pending`, () =>
        HttpResponse.json([makeFirstAscensionistLinkRequest({ id: 1 })]),
      ),
      http.get(`${API_URL}/first-ascensionists/pending`, () => HttpResponse.json([])),
    );

    renderProbe(ALL_PERMISSIONS);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('done'));
    // routes(1) + areas(1) + areaExperts(0, failed) + faLinks(1) + firstAscensionists(0) = 3 — the failure didn't blank the rest.
    expect(screen.getByTestId('total')).toHaveTextContent('3');
  });

  it('fetches nothing when the user has none of the five validate/approve permissions', async () => {
    // No handlers registered for routes/areas/area-experts/first-ascensionist-links/first-ascensionists — an
    // accidental fetch here would fail the test via onUnhandledRequest: 'error'.
    renderProbe([]);
    await act(async () => {});

    expect(screen.getByTestId('total')).toHaveTextContent('0');
  });
});
