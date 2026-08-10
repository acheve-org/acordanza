import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea, makeRouteDetail } from '../test/factories';
import { RouteFormPage } from './RouteFormPage';

function renderCreateForm() {
  return renderWithProviders(
    <Routes>
      <Route path="/routes/new" element={<RouteFormPage />} />
    </Routes>,
    { initialEntries: ['/routes/new'] },
  );
}

function renderEditForm(routeId: number) {
  return renderWithProviders(
    <Routes>
      <Route path="/routes/:id/edit" element={<RouteFormPage />} />
    </Routes>,
    { initialEntries: [`/routes/${routeId}/edit`] },
  );
}

async function selectArea(user: ReturnType<typeof userEvent.setup>, areaName: string) {
  await user.click(screen.getByPlaceholderText('Buscar sector…'));
  await user.click(await screen.findByRole('button', { name: areaName }));
}

describe('RouteFormPage', () => {
  it('renders the five new fields and no Etiquetas field', () => {
    renderCreateForm();

    expect(screen.getByLabelText(/Longitud/)).toBeInTheDocument();
    expect(screen.getByLabelText('Grado obligatorio')).toBeInTheDocument();
    expect(screen.getByLabelText('Material recomendado')).toBeInTheDocument();
    expect(screen.getByLabelText('Material opcional')).toBeInTheDocument();
    expect(screen.getByText('Enlaces')).toBeInTheDocument();
    expect(screen.queryByLabelText(/Etiquetas/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Etiquetas/)).not.toBeInTheDocument();
  });

  it('posts length as a number and the three text fields when submitting', async () => {
    const user = userEvent.setup();
    let postedBody: Record<string, unknown> | undefined;
    server.use(
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([makeArea({ id: 5, name: 'Peña Rúa', validationStatus: 'approved' })]),
      ),
      http.post(`${API_URL}/routes`, async ({ request }) => {
        postedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 1 });
      }),
    );

    renderCreateForm();

    await user.type(screen.getByLabelText('Nombre'), 'Travesía del Gato');
    await user.type(screen.getByLabelText('Grado'), '6a');
    await user.clear(screen.getByLabelText(/Longitud/));
    await user.type(screen.getByLabelText(/Longitud/), '30');
    await user.type(screen.getByLabelText('Grado obligatorio'), 'V+');
    await user.type(screen.getByLabelText('Material recomendado'), '12 cintas');
    await user.type(screen.getByLabelText('Material opcional'), 'friends medianos');
    await selectArea(user, 'Peña Rúa');

    await user.click(screen.getByRole('button', { name: 'Guardar vía' }));

    await waitFor(() => expect(postedBody).toBeDefined());
    expect(postedBody).toMatchObject({
      length: 30,
      obligatoryGrade: 'V+',
      recommendedGear: '12 cintas',
      optionalGear: 'friends medianos',
    });
  });

  it('posts null orientation and gear when left as "Sin especificar"', async () => {
    const user = userEvent.setup();
    let postedBody: Record<string, unknown> | undefined;
    server.use(
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([makeArea({ id: 5, name: 'Peña Rúa', validationStatus: 'approved' })]),
      ),
      http.post(`${API_URL}/routes`, async ({ request }) => {
        postedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 1 });
      }),
    );

    renderCreateForm();

    await user.type(screen.getByLabelText('Nombre'), 'Travesía del Gato');
    await user.type(screen.getByLabelText('Grado'), '6a');
    await user.clear(screen.getByLabelText(/Longitud/));
    await user.type(screen.getByLabelText(/Longitud/), '30');
    await selectArea(user, 'Peña Rúa');

    await user.click(screen.getByRole('button', { name: 'Guardar vía' }));

    await waitFor(() => expect(postedBody).toBeDefined());
    expect(postedBody).toMatchObject({ orientation: null, gear: null });
  });

  it('adding two links then removing one posts exactly the remaining URL', async () => {
    const user = userEvent.setup();
    let postedBody: Record<string, unknown> | undefined;
    server.use(
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([makeArea({ id: 5, name: 'Peña Rúa', validationStatus: 'approved' })]),
      ),
      http.post(`${API_URL}/routes`, async ({ request }) => {
        postedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 1 });
      }),
    );

    renderCreateForm();

    await user.type(screen.getByLabelText('Nombre'), 'Travesía del Gato');
    await user.type(screen.getByLabelText('Grado'), '6a');
    await user.clear(screen.getByLabelText(/Longitud/));
    await user.type(screen.getByLabelText(/Longitud/), '30');
    await selectArea(user, 'Peña Rúa');

    await user.click(screen.getByRole('button', { name: '+ Añadir enlace' }));
    await user.click(screen.getByRole('button', { name: '+ Añadir enlace' }));
    const linkInputs = screen.getAllByPlaceholderText('https://…');
    await user.type(linkInputs[0]!, 'https://example.com/a');
    await user.type(linkInputs[1]!, 'https://example.com/b');

    await user.click(screen.getAllByRole('button', { name: 'Eliminar enlace' })[0]!);

    await user.click(screen.getByRole('button', { name: 'Guardar vía' }));

    await waitFor(() => expect(postedBody).toBeDefined());
    expect(postedBody!.links).toEqual(['https://example.com/b']);
  });

  it('seeds every new field from GET /routes/{id} when editing', async () => {
    const routeDetail = makeRouteDetail({
      id: 7,
      areaId: 1,
      length: 45,
      obligatoryGrade: 'V+',
      recommendedGear: '12 cintas',
      optionalGear: 'friends medianos',
      links: ['https://example.com/a', 'https://example.com/b'],
    });
    server.use(
      http.get(`${API_URL}/routes/7`, () => HttpResponse.json(routeDetail)),
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([makeArea({ id: 1, name: routeDetail.areaName, validationStatus: 'approved' })]),
      ),
      http.get(`${API_URL}/first-ascensionists`, () => HttpResponse.json(routeDetail.firstAscensionists)),
    );

    renderEditForm(7);

    expect(await screen.findByDisplayValue('45')).toBeInTheDocument();
    expect(screen.getByDisplayValue('V+')).toBeInTheDocument();
    expect(screen.getByDisplayValue('12 cintas')).toBeInTheDocument();
    expect(screen.getByDisplayValue('friends medianos')).toBeInTheDocument();
    expect(screen.getByDisplayValue('https://example.com/a')).toBeInTheDocument();
    expect(screen.getByDisplayValue('https://example.com/b')).toBeInTheDocument();
  });
});
