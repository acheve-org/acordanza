import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea, makeTopoImport } from '../test/factories';
import { MyImportsPage } from './MyImportsPage';

type TopoImport = ReturnType<typeof makeTopoImport>;

function mockImports(items: TopoImport[]) {
  server.use(http.get(`${API_URL}/topo-imports/mine`, () => HttpResponse.json(items)));
}

describe('MyImportsPage', () => {
  it('renders each status with its Spanish label', async () => {
    mockImports([
      makeTopoImport({ id: 1, fileName: 'pendiente.jpg', status: 'pending' }),
      makeTopoImport({
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
      makeTopoImport({
        id: 3,
        fileName: 'descartado.jpg',
        status: 'discarded',
        outcome: 'No parece un topo de escalada',
      }),
      makeTopoImport({
        id: 4,
        fileName: 'fallo.jpg',
        status: 'failed',
        outcome: 'No se pudo leer el topo, inténtalo de nuevo',
      }),
    ]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('Leyendo el topo')).toBeInTheDocument();
    expect(screen.getByText('Completado')).toBeInTheDocument();
    expect(screen.getByText('Descartado')).toBeInTheDocument();
    expect(screen.getByText('Falló')).toBeInTheDocument();
  });

  // Nothing is silently discarded: the reason is the whole reason a discarded row exists
  // at all, and the retry is what makes it actionable.
  it('shows a discarded row’s reason and its retry affordance', async () => {
    mockImports([
      makeTopoImport({ id: 1, status: 'discarded', outcome: 'El topo no indica el nombre de la vía' }),
    ]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('El topo no indica el nombre de la vía')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Volver a intentarlo' })).toBeInTheDocument();
  });

  // Only a discard is retryable from here — a failure is retried by the queue itself, and
  // offering "try again" on a completed row would invite a duplicate upload.
  it('offers no retry on a completed row', async () => {
    mockImports([makeTopoImport({ id: 1, status: 'completed', routeId: 7, routeName: 'La Loca' })]);

    renderWithProviders(<MyImportsPage />);

    await screen.findByText('Completado');
    expect(screen.queryByRole('button', { name: 'Volver a intentarlo' })).not.toBeInTheDocument();
  });

  it('links a completed row to the route it produced', async () => {
    mockImports([
      makeTopoImport({
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

  it('shows the empty state when nothing has been uploaded', async () => {
    mockImports([]);

    renderWithProviders(<MyImportsPage />);

    expect(await screen.findByText('Todavía no has subido ningún topo')).toBeInTheDocument();
  });

  it('uploads the file with the picked sector and shows the inline wait note', async () => {
    mockImports([]);
    server.use(
      http.get(`${API_URL}/areas`, () => HttpResponse.json([makeArea({ id: 3, name: 'Peña Rúa', validationStatus: 'approved' })])),
    );

    // The raw multipart body, not request.formData(): undici's parser asserts on a real
    // File and jsdom's isn't one, so calling it here throws inside the handler and the
    // request surfaces as a 500 that has nothing to do with the page.
    let postedBody: string | null = null;
    server.use(
      http.post(`${API_URL}/topo-imports`, async ({ request }) => {
        postedBody = await request.text();
        return HttpResponse.json(makeTopoImport({ id: 9 }), { status: 202 });
      }),
    );

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has subido ningún topo');

    const file = new File(['topo'], 'topo.jpg', { type: 'image/jpeg' });
    fireEvent.change(screen.getByLabelText('Topo'), { target: { files: [file] } });

    // The sector is picked, never inferred — so the request can't go out without it.
    fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Peña Rúa' }));

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

  it('cannot submit without both a file and a sector', async () => {
    mockImports([]);

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has subido ningún topo');

    expect(screen.getByRole('button', { name: 'Leer el topo' })).toBeDisabled();

    const file = new File(['topo'], 'topo.jpg', { type: 'image/jpeg' });
    fireEvent.change(screen.getByLabelText('Topo'), { target: { files: [file] } });

    // Still disabled: a file alone isn't enough.
    expect(screen.getByRole('button', { name: 'Leer el topo' })).toBeDisabled();
  });

  it('surfaces an upload error in Spanish', async () => {
    mockImports([]);
    server.use(
      http.get(`${API_URL}/areas`, () => HttpResponse.json([makeArea({ id: 3, name: 'Peña Rúa', validationStatus: 'approved' })])),
      http.post(`${API_URL}/topo-imports`, () =>
        HttpResponse.json({ error: 'la imagen es demasiado grande (máximo 15 MB)' }, { status: 400 }),
      ),
    );

    renderWithProviders(<MyImportsPage />);
    await screen.findByText('Todavía no has subido ningún topo');

    const file = new File(['topo'], 'topo.jpg', { type: 'image/jpeg' });
    fireEvent.change(screen.getByLabelText('Topo'), { target: { files: [file] } });
    fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Peña Rúa' }));
    fireEvent.click(screen.getByRole('button', { name: 'Leer el topo' }));

    expect(await screen.findByText('la imagen es demasiado grande (máximo 15 MB)')).toBeInTheDocument();
    expect(
      screen.queryByText('Estamos leyendo el topo. El resultado aparecerá aquí en unos minutos.'),
    ).not.toBeInTheDocument();
  });
});
