import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea, makeRouteImport } from '../test/factories';
import { MyImportsPage } from './MyImportsPage';

type RouteImport = ReturnType<typeof makeRouteImport>;

function mockImports(items: RouteImport[]) {
  server.use(http.get(`${API_URL}/route-imports/mine`, () => HttpResponse.json(items)));
}

function mockAreas() {
  server.use(
    http.get(`${API_URL}/areas`, () =>
      HttpResponse.json([makeArea({ id: 3, name: 'Peña Rúa', validationStatus: 'approved' })]),
    ),
  );
}

function pickTheSector() {
  fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));
  return screen.findByRole('button', { name: 'Peña Rúa' });
}

describe('MyImportsPage', () => {
  it('renders each status with its Spanish label', async () => {
    mockImports([
      makeRouteImport({ id: 1, fileName: 'pendiente.jpg', status: 'pending' }),
      makeRouteImport({
        id: 2,
        fileName: 'hecho.jpg',
        status: 'completed',
        outcome: 'Vía creada y pendiente de validación',
        routeId: 7,
        routeName: 'La Loca',
        routeSlug: 'la-loca',
        routeAreaSlugPath: 'pena-rua/sector-1',
        routeWasCreated: true,
      }),
      makeRouteImport({
        id: 3,
        fileName: 'descartado.jpg',
        status: 'discarded',
        outcome: 'No parece un topo de escalada',
      }),
      makeRouteImport({
        id: 4,
        fileName: 'fallo.jpg',
        status: 'failed',
        outcome: 'No se pudo completar la lectura, inténtalo de nuevo',
      }),
    ]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('Leyendo')).toBeInTheDocument();
    expect(screen.getByText('Completado')).toBeInTheDocument();
    expect(screen.getByText('Descartado')).toBeInTheDocument();
    expect(screen.getByText('Falló')).toBeInTheDocument();
  });

  // One list, both kinds: a photo row is titled by its filename, a link row by the URL.
  it('shows both kinds in one list, each titled by its own artifact', async () => {
    mockImports([
      makeRouteImport({ id: 1, kind: 'image', fileName: 'topo.jpg' }),
      makeRouteImport({
        id: 2,
        kind: 'url',
        fileName: null,
        sourceUrl: 'https://blog.example.com/apertura-la-loca',
      }),
    ]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('topo.jpg')).toBeInTheDocument();
    expect(screen.getByText('https://blog.example.com/apertura-la-loca')).toBeInTheDocument();
    expect(screen.getByText(/Foto ·/)).toBeInTheDocument();
    expect(screen.getByText(/Enlace ·/)).toBeInTheDocument();
  });

  // Nothing is silently discarded: the reason is the whole reason a discarded row exists
  // at all, and the retry is what makes it actionable.
  it('shows a discarded row’s reason and its retry affordance', async () => {
    mockImports([
      makeRouteImport({ id: 1, status: 'discarded', outcome: 'El topo no indica el nombre de la vía' }),
    ]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('El topo no indica el nombre de la vía')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Volver a intentarlo' })).toBeInTheDocument();
  });

  // Retrying a discarded link puts the URL back in the form rather than re-enqueueing —
  // the user usually wants to fix the link or the sector first, and it's one code path
  // with the photo kind's re-upload.
  it('retrying a discarded link switches to the link form and refills the url', async () => {
    mockImports([
      makeRouteImport({
        id: 1,
        kind: 'url',
        fileName: null,
        sourceUrl: 'https://blog.example.com/apertura-la-loca',
        status: 'discarded',
        outcome: 'La página no habla de una vía de escalada',
      }),
    ]);

    renderWithProviders(<MyImportsPage />);

    fireEvent.click(await screen.findByRole('button', { name: 'Volver a intentarlo' }));

    expect(screen.getByLabelText('Enlace')).toHaveValue('https://blog.example.com/apertura-la-loca');
  });

  // Only a discard is retryable from here — a failure is retried by the queue itself, and
  // offering "try again" on a completed row would invite a duplicate submission.
  it('offers no retry on a completed row', async () => {
    mockImports([makeRouteImport({ id: 1, status: 'completed', routeId: 7, routeName: 'La Loca' })]);

    renderWithProviders(<MyImportsPage />);

    await screen.findByText('Completado');
    expect(screen.queryByRole('button', { name: 'Volver a intentarlo' })).not.toBeInTheDocument();
  });

  it('links a completed row to the route it produced', async () => {
    mockImports([
      makeRouteImport({
        id: 1,
        status: 'completed',
        routeId: 7,
        routeName: 'La Loca',
        routeSlug: 'la-loca',
        routeAreaSlugPath: 'pena-rua/sector-1',
      }),
    ]);

    renderWithProviders(<MyImportsPage />);

    const link = await screen.findByRole('link', { name: 'La Loca' });
    expect(link).toHaveAttribute('href', '/routes/pena-rua/sector-1/la-loca');
  });

  it('shows the empty state when nothing has been imported', async () => {
    mockImports([]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('Todavía no has importado ninguna vía')).toBeInTheDocument();
  });

  // The switch changes which field the form shows — that's the whole reason it's a switch
  // between two forms rather than a value being picked.
  it('the mode switch shows the right field', async () => {
    mockImports([]);

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has importado ninguna vía');

    expect(screen.getByLabelText('Topo')).toBeInTheDocument();
    expect(screen.queryByLabelText('Enlace')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Desde un enlace' }));

    expect(screen.getByLabelText('Enlace')).toBeInTheDocument();
    expect(screen.queryByLabelText('Topo')).not.toBeInTheDocument();
  });

  it('uploads the file with the picked sector and shows the inline wait note', async () => {
    mockImports([]);
    mockAreas();

    // The raw multipart body, not request.formData(): undici's parser asserts on a real
    // File and jsdom's isn't one, so calling it here throws inside the handler and the
    // request surfaces as a 500 that has nothing to do with the page.
    let postedBody: string | null = null;
    server.use(
      http.post(`${API_URL}/route-imports/image`, async ({ request }) => {
        postedBody = await request.text();
        return HttpResponse.json(makeRouteImport({ id: 9 }), { status: 202 });
      }),
    );

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has importado ninguna vía');

    const file = new File(['topo'], 'topo.jpg', { type: 'image/jpeg' });
    fireEvent.change(screen.getByLabelText('Topo'), { target: { files: [file] } });

    // The sector is picked, never inferred — so the request can't go out without it.
    fireEvent.click(await pickTheSector());
    fireEvent.click(screen.getByRole('button', { name: 'Leer el topo' }));

    // No polling and no progress bar — the row is in the list and a reload updates it.
    expect(
      await screen.findByText('Estamos leyendo el topo. El resultado aparecerá aquí en unos minutos.'),
    ).toBeInTheDocument();

    // Both parts are there, and areaId carries the sector picked above rather than a
    // default. (The filename arrives as "blob" — jsdom's File loses it crossing into
    // undici — so there's nothing to assert about it here.)
    await waitFor(() => expect(postedBody).not.toBeNull());
    expect(postedBody).toContain('name="file"');
    expect(postedBody).toContain('Content-Type: image/jpeg');
    expect(postedBody).toMatch(/name="areaId"\r?\n\r?\n3/);
  });

  it('posts the url with the picked sector and shows the inline wait note', async () => {
    mockImports([]);
    mockAreas();

    let posted: { url?: string; areaId?: number } | null = null;
    server.use(
      http.post(`${API_URL}/route-imports/url`, async ({ request }) => {
        posted = (await request.json()) as { url: string; areaId: number };
        return HttpResponse.json(makeRouteImport({ id: 9, kind: 'url' }), { status: 202 });
      }),
    );

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has importado ninguna vía');

    fireEvent.click(screen.getByRole('button', { name: 'Desde un enlace' }));
    fireEvent.change(screen.getByLabelText('Enlace'), {
      target: { value: '  https://blog.example.com/apertura-la-loca  ' },
    });
    fireEvent.click(await pickTheSector());
    fireEvent.click(screen.getByRole('button', { name: 'Leer la página' }));

    expect(
      await screen.findByText('Estamos leyendo la página. El resultado aparecerá aquí en unos minutos.'),
    ).toBeInTheDocument();

    await waitFor(() => expect(posted).not.toBeNull());
    expect(posted).toEqual({ url: 'https://blog.example.com/apertura-la-loca', areaId: 3 });
  });

  it('cannot submit without both an artifact and a sector', async () => {
    mockImports([]);

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has importado ninguna vía');

    expect(screen.getByRole('button', { name: 'Leer el topo' })).toBeDisabled();

    const file = new File(['topo'], 'topo.jpg', { type: 'image/jpeg' });
    fireEvent.change(screen.getByLabelText('Topo'), { target: { files: [file] } });

    // Still disabled: a file alone isn't enough.
    expect(screen.getByRole('button', { name: 'Leer el topo' })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: 'Desde un enlace' }));
    expect(screen.getByRole('button', { name: 'Leer la página' })).toBeDisabled();
  });

  it('surfaces a submission error in Spanish', async () => {
    mockImports([]);
    mockAreas();
    server.use(
      http.post(`${API_URL}/route-imports/url`, () =>
        HttpResponse.json({ error: 'algún enlace es demasiado largo' }, { status: 400 }),
      ),
    );

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has importado ninguna vía');

    fireEvent.click(screen.getByRole('button', { name: 'Desde un enlace' }));
    // A URL the browser's own type="url" validation accepts, so the submit actually
    // fires and what's under test is the server's Spanish error reaching the page.
    fireEvent.change(screen.getByLabelText('Enlace'), {
      target: { value: `https://example.com/${'a'.repeat(2100)}` },
    });
    fireEvent.click(await pickTheSector());
    fireEvent.click(screen.getByRole('button', { name: 'Leer la página' }));

    expect(await screen.findByText('algún enlace es demasiado largo')).toBeInTheDocument();
    expect(
      screen.queryByText('Estamos leyendo la página. El resultado aparecerá aquí en unos minutos.'),
    ).not.toBeInTheDocument();
  });
});
