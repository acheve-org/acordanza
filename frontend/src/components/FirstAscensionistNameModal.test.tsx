import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { FirstAscensionistNameModal } from './FirstAscensionistNameModal';

describe('FirstAscensionistNameModal', () => {
  it('creates a new first ascensionist and calls onSaved', async () => {
    let body: unknown;
    server.use(
      http.post(`${API_URL}/first-ascensionists`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    const onSaved = vi.fn();
    renderWithProviders(<FirstAscensionistNameModal onClose={vi.fn()} onSaved={onSaved} />);

    expect(screen.getByRole('heading', { name: 'Nuevo aperturista' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Nuevo Aperturista' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear' }));

    await waitFor(() => expect(body).toEqual({ name: 'Nuevo Aperturista' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it('renames an existing first ascensionist via PUT, prefilled with its name', async () => {
    let body: unknown;
    server.use(
      http.put(`${API_URL}/first-ascensionists/3`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    const onSaved = vi.fn();
    renderWithProviders(
      <FirstAscensionistNameModal firstAscensionist={{ id: 3, name: 'Alex' }} onClose={vi.fn()} onSaved={onSaved} />,
    );

    expect(screen.getByRole('heading', { name: 'Editar aperturista' })).toBeInTheDocument();
    expect(screen.getByLabelText('Nombre')).toHaveValue('Alex');

    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Alex Ruiz' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(body).toEqual({ name: 'Alex Ruiz' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it('falls back to the create-specific message on failure', async () => {
    server.use(http.post(`${API_URL}/first-ascensionists`, () => HttpResponse.error()));
    renderWithProviders(<FirstAscensionistNameModal onClose={vi.fn()} onSaved={vi.fn()} />);

    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Nuevo' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear' }));

    await waitFor(() => expect(screen.getByText('No se pudo crear el aperturista')).toBeInTheDocument());
  });

  it('falls back to the rename-specific message on failure', async () => {
    server.use(http.put(`${API_URL}/first-ascensionists/3`, () => HttpResponse.error()));
    renderWithProviders(
      <FirstAscensionistNameModal firstAscensionist={{ id: 3, name: 'Alex' }} onClose={vi.fn()} onSaved={vi.fn()} />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(screen.getByText('No se pudo actualizar el aperturista')).toBeInTheDocument());
  });
});
