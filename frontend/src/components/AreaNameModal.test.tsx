import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { AreaNameModal } from './AreaNameModal';

describe('AreaNameModal', () => {
  it('creates a top-level area and calls onSaved', async () => {
    let body: unknown;
    server.use(
      http.post(`${API_URL}/areas`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    const onSaved = vi.fn();
    renderWithProviders(<AreaNameModal parentAreaId={null} onClose={vi.fn()} onSaved={onSaved} />);

    expect(screen.getByRole('heading', { name: 'Nuevo sector principal' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Peña Nueva' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear' }));

    await waitFor(() => expect(body).toEqual({ name: 'Peña Nueva', parentAreaId: null }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it('titles a subsector differently from a top-level area', () => {
    renderWithProviders(<AreaNameModal parentAreaId={5} onClose={vi.fn()} onSaved={vi.fn()} />);
    expect(screen.getByRole('heading', { name: 'Nuevo subsector' })).toBeInTheDocument();
  });

  it('renames an existing area via PUT, prefilled with its name', async () => {
    let body: unknown;
    server.use(
      http.put(`${API_URL}/areas/7`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    const onSaved = vi.fn();
    renderWithProviders(
      <AreaNameModal parentAreaId={null} area={{ id: 7, name: 'Peña Rúa' }} onClose={vi.fn()} onSaved={onSaved} />,
    );

    expect(screen.getByRole('heading', { name: 'Editar sector' })).toBeInTheDocument();
    expect(screen.getByLabelText('Nombre')).toHaveValue('Peña Rúa');

    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Peña Rúa Norte' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(body).toEqual({ name: 'Peña Rúa Norte', parentAreaId: null }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it('falls back to the create-specific message on failure', async () => {
    server.use(http.post(`${API_URL}/areas`, () => HttpResponse.error()));
    renderWithProviders(<AreaNameModal parentAreaId={null} onClose={vi.fn()} onSaved={vi.fn()} />);

    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Peña Nueva' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear' }));

    await waitFor(() => expect(screen.getByText('No se pudo crear el sector')).toBeInTheDocument());
  });

  it('falls back to the rename-specific message on failure', async () => {
    server.use(http.put(`${API_URL}/areas/7`, () => HttpResponse.error()));
    renderWithProviders(
      <AreaNameModal parentAreaId={null} area={{ id: 7, name: 'Peña Rúa' }} onClose={vi.fn()} onSaved={vi.fn()} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(screen.getByText('No se pudo actualizar el sector')).toBeInTheDocument());
  });
});
