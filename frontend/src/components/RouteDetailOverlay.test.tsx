import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRouteDetail, makeTopo } from '../test/factories';
import { Permissions } from '../api/permissions';
import { RouteDetailOverlay } from './RouteDetailOverlay';

function LocationProbe({ onLocation }: { onLocation: (pathname: string) => void }) {
  onLocation(useLocation().pathname);
  return null;
}

function renderOverlay(permissions: string[] = [], onLocation: (pathname: string) => void = () => {}) {
  return renderWithProviders(
    <Routes>
      {/* Exact "routes" stands in for the background page (SearchPage in production) —
          ranked above the splat below, same as App.tsx, so closing the overlay lands
          here instead of re-matching the overlay's own route. */}
      <Route path="routes" element={<LocationProbe onLocation={onLocation} />} />
      <Route path="routes/*" element={<RouteDetailOverlay />} />
    </Routes>,
    {
      auth: { permissions },
      initialEntries: [
        {
          pathname: '/routes/pena-rua/travesia-del-gato',
          state: { backgroundLocation: { pathname: '/routes' } },
        },
      ],
    },
  );
}

describe('RouteDetailOverlay', () => {
  it('fetches the route by its full slug path and renders its detail', async () => {
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, ({ request }) => {
        expect(request.url).toContain('/routes/by-slug-path/pena-rua/travesia-del-gato');
        return HttpResponse.json(makeRouteDetail({ name: 'Travesía del Gato' }));
      }),
    );

    renderOverlay();

    expect(await screen.findByRole('heading', { name: 'Travesía del Gato' })).toBeInTheDocument();
  });

  it('deleting the route calls DELETE /routes/{id} and closes to the backgroundLocation', async () => {
    const user = userEvent.setup();
    let deleteCalled = false;
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7 })),
      ),
      http.delete(`${API_URL}/routes/7`, () => {
        deleteCalled = true;
        return HttpResponse.json({ ok: true });
      }),
    );

    let pathname = '';
    renderOverlay([Permissions.routeDelete], (p) => {
      pathname = p;
    });

    await screen.findByRole('heading', { name: 'Travesía del Gato' });
    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    const confirmButtons = await screen.findAllByRole('button', { name: 'Eliminar' });
    await user.click(confirmButtons[confirmButtons.length - 1]!);

    await waitFor(() => expect(deleteCalled).toBe(true));
    await waitFor(() => expect(pathname).toBe('/routes'));
  });

  it('validating the route calls POST /routes/{id}/validate', async () => {
    const user = userEvent.setup();
    let validateCalled = false;
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7, canValidate: true, validationStatus: 'pending' })),
      ),
      http.post(`${API_URL}/routes/7/validate`, () => {
        validateCalled = true;
        return HttpResponse.json({ ok: true });
      }),
    );

    renderOverlay();

    await user.click(await screen.findByRole('button', { name: 'Validar' }));

    await waitFor(() => expect(validateCalled).toBe(true));
  });

  it('rejecting the route calls POST /routes/{id}/reject with the typed reason', async () => {
    const user = userEvent.setup();
    let rejectedReason = '';
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7, canValidate: true, validationStatus: 'pending' })),
      ),
      http.post(`${API_URL}/routes/7/reject`, async ({ request }) => {
        rejectedReason = ((await request.json()) as { reason: string }).reason;
        return HttpResponse.json({ ok: true });
      }),
    );

    renderOverlay();

    await user.click(await screen.findByRole('button', { name: 'Rechazar' }));
    await user.type(screen.getByLabelText('Motivo del rechazo'), 'Duplicada');
    await user.click(screen.getByRole('button', { name: 'Rechazar solicitud' }));

    await waitFor(() => expect(rejectedReason).toBe('Duplicada'));
  });

  it('adding a tick calls POST /routes/{id}/ticks', async () => {
    const user = userEvent.setup();
    let posted: Record<string, unknown> | undefined;
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7 })),
      ),
      http.post(`${API_URL}/routes/7/ticks`, async ({ request }) => {
        posted = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 1 });
      }),
    );

    renderOverlay([Permissions.tickCreate]);

    await user.click(await screen.findByRole('button', { name: '+ Registrar ascenso' }));
    await user.type(screen.getByLabelText('Fecha'), '2026-01-01');
    await user.click(screen.getByRole('button', { name: 'Registrar' }));

    await waitFor(() => expect(posted).toBeDefined());
    expect(posted).toMatchObject({ date: '2026-01-01' });
  });

  it('uploading a topo calls POST /routes/{id}/topos', async () => {
    const user = userEvent.setup();
    let uploadCalled = false;
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7 })),
      ),
      http.post(`${API_URL}/routes/7/topos`, () => {
        uploadCalled = true;
        return HttpResponse.json(makeTopo());
      }),
    );

    renderOverlay();
    await screen.findByRole('heading', { name: 'Travesía del Gato' });

    const file = new File(['x'], 'topo.jpg', { type: 'image/jpeg' });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, file);

    await waitFor(() => expect(uploadCalled).toBe(true));
  });

  it('deleting a topo calls DELETE /topos/{id}', async () => {
    const user = userEvent.setup();
    let deleteCalled = false;
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7, topos: [makeTopo({ id: 99 })] })),
      ),
      http.delete(`${API_URL}/topos/99`, () => {
        deleteCalled = true;
        return HttpResponse.json({ ok: true });
      }),
    );

    renderOverlay();
    await screen.findByRole('heading', { name: 'Travesía del Gato' });

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));

    await waitFor(() => expect(deleteCalled).toBe(true));
  });

  it('closing (Escape) navigates back to the backgroundLocation', async () => {
    const user = userEvent.setup();
    server.use(
      http.get(`${API_URL}/routes/by-slug-path/pena-rua/travesia-del-gato`, () =>
        HttpResponse.json(makeRouteDetail({ id: 7 })),
      ),
    );

    let pathname = '';
    renderOverlay([], (p) => {
      pathname = p;
    });

    await screen.findByRole('heading', { name: 'Travesía del Gato' });
    await user.keyboard('{Escape}');

    await waitFor(() => expect(pathname).toBe('/routes'));
  });
});
