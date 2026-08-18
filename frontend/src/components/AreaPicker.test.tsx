import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea, makeAreaTreeNode } from '../test/factories';
import { Permissions } from '../api/permissions';
import { AreaPicker } from './AreaPicker';

function mockTree(nodes: ReturnType<typeof makeAreaTreeNode>[]) {
  server.use(http.get(`${API_URL}/areas/tree`, () => HttpResponse.json(nodes)));
}

// Pins frontend/CLAUDE.md's "modal/dropdown data is fetched by the thing that needs it, when
// it opens" rule for AreaPicker specifically — with `onUnhandledRequest: 'error'` (see
// src/test/setup.ts), an accidental mount-time prefetch would fail these tests outright.
describe('AreaPicker deferred fetching', () => {
  it('does not fetch /areas/tree on mount with nothing selected', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/areas/tree`, () => {
        calls++;
        return HttpResponse.json([]);
      }),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} />);
    await act(async () => {});

    expect(calls).toBe(0);
  });

  it('fetches /areas/tree exactly once when the picker opens', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/areas/tree`, () => {
        calls++;
        return HttpResponse.json([makeAreaTreeNode({ id: 1, name: 'Peña Rúa' })]);
      }),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} />);
    fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));

    await waitFor(() => expect(calls).toBe(1));
    expect(await screen.findByRole('treeitem', { name: 'Peña Rúa' })).toBeInTheDocument();
  });

  it('fetches /areas/tree on mount when a value is already selected, to resolve its label', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/areas/tree`, () => {
        calls++;
        return HttpResponse.json([makeAreaTreeNode({ id: 1, name: 'Peña Rúa' })]);
      }),
    );

    renderWithProviders(<AreaPicker value={1} onChange={() => {}} />);

    await waitFor(() => expect(calls).toBe(1));
    await waitFor(() => expect(screen.getByPlaceholderText('Buscar sector…')).toHaveValue('Peña Rúa'));
  });
});

describe('AreaPicker validation status suffix', () => {
  it('flags a non-approved area with its validation status, since the tree is approved-or-mine', async () => {
    mockTree([makeAreaTreeNode({ id: 1, name: 'Peña Rúa', validationStatus: 'pending' })]);

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} />);
    fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));

    expect(await screen.findByRole('treeitem', { name: 'Peña Rúa (Pendiente de validación)' })).toBeInTheDocument();
  });
});

describe('AreaPicker subtree hint', () => {
  it('shows how many subsectors are included in filter mode, when the selected area has children', async () => {
    mockTree([
      makeAreaTreeNode({ id: 1, name: 'Peña Rúa', parentAreaId: null }),
      makeAreaTreeNode({ id: 2, name: 'Sector Norte', parentAreaId: 1 }),
    ]);

    renderWithProviders(<AreaPicker value={1} onChange={() => {}} allowClear />);

    expect(await screen.findByText('Incluye 1 subsector')).toBeInTheDocument();
  });

  it('does not show the subtree hint in assign mode', async () => {
    mockTree([
      makeAreaTreeNode({ id: 1, name: 'Peña Rúa', parentAreaId: null }),
      makeAreaTreeNode({ id: 2, name: 'Sector Norte', parentAreaId: 1 }),
    ]);

    renderWithProviders(<AreaPicker value={1} onChange={() => {}} />);

    await waitFor(() => expect(screen.getByPlaceholderText('Buscar sector…')).toHaveValue('Peña Rúa'));
    expect(screen.queryByText(/Incluye/)).not.toBeInTheDocument();
  });
});

describe('AreaPicker contextual create', () => {
  it('posts the clicked rows id as parentAreaId', async () => {
    const user = userEvent.setup();
    mockTree([makeAreaTreeNode({ id: 1, name: 'Peña Rúa', parentAreaId: null })]);
    let lastBody: unknown;
    server.use(
      http.post(`${API_URL}/areas`, async ({ request }) => {
        lastBody = await request.json();
        return HttpResponse.json(makeArea({ id: 99, name: 'Sector Nuevo', parentAreaId: 1 }));
      }),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} allowCreate />, {
      auth: { permissions: [Permissions.areaCreate] },
    });

    await user.click(screen.getByPlaceholderText('Buscar sector…'));
    await user.click(screen.getByText('Ver todas las zonas'));
    await user.click(screen.getByRole('button', { name: 'Añadir subsector aquí' }));
    await user.type(screen.getByPlaceholderText('Nombre del nuevo sector'), 'Sector Nuevo');
    await user.click(screen.getByRole('button', { name: 'Añadir' }));

    await waitFor(() => expect(lastBody).toEqual({ name: 'Sector Nuevo', parentAreaId: 1 }));
  });

  it('posts null as parentAreaId from the foot button', async () => {
    const user = userEvent.setup();
    mockTree([makeAreaTreeNode({ id: 1, name: 'Peña Rúa', parentAreaId: null })]);
    let lastBody: unknown;
    server.use(
      http.post(`${API_URL}/areas`, async ({ request }) => {
        lastBody = await request.json();
        return HttpResponse.json(makeArea({ id: 99, name: 'Zona Nueva', parentAreaId: null }));
      }),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} allowCreate />, {
      auth: { permissions: [Permissions.areaCreate] },
    });

    await user.click(screen.getByPlaceholderText('Buscar sector…'));
    await user.click(screen.getByText('Ver todas las zonas'));
    await user.click(screen.getByRole('button', { name: '+ Añadir zona nueva' }));
    await user.type(screen.getByPlaceholderText('Nombre del nuevo sector'), 'Zona Nueva');
    await user.click(screen.getByRole('button', { name: 'Añadir' }));

    await waitFor(() => expect(lastBody).toEqual({ name: 'Zona Nueva', parentAreaId: null }));
  });
});

describe('AreaPicker recents', () => {
  it('persist across a remount', async () => {
    localStorage.setItem('acordanza.recentAreas', JSON.stringify([2]));
    mockTree([
      makeAreaTreeNode({ id: 1, name: 'Otra Zona', parentAreaId: null }),
      makeAreaTreeNode({ id: 2, name: 'Peña Rúa', parentAreaId: null }),
    ]);

    const user = userEvent.setup();
    renderWithProviders(<AreaPicker value={null} onChange={() => {}} allowClear />);
    await user.click(screen.getByPlaceholderText('Buscar sector…'));

    expect(await screen.findByRole('treeitem', { name: 'Peña Rúa' })).toBeInTheDocument();
  });

  it('are padded by the highest route count when localStorage is empty', async () => {
    mockTree([
      makeAreaTreeNode({ id: 1, name: 'Zona Pequeña', parentAreaId: null, routeCount: 1 }),
      makeAreaTreeNode({ id: 2, name: 'Zona Grande', parentAreaId: null, routeCount: 9 }),
    ]);

    const user = userEvent.setup();
    renderWithProviders(<AreaPicker value={null} onChange={() => {}} allowClear />);
    await user.click(screen.getByPlaceholderText('Buscar sector…'));

    const rows = await screen.findAllByRole('treeitem');
    // rows[0] is the "Cualquier sector" leadingOption, pinned above the head rows.
    expect(rows[1]).toHaveAccessibleName('Zona Grande');
    expect(rows[2]).toHaveAccessibleName('Zona Pequeña');
  });
});
