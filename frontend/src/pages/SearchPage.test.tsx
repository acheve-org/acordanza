import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeRoute, makeRouteDetail } from '../test/factories';
import { Permissions } from '../api/permissions';
import { SearchPage } from './SearchPage';

function mockGrades(grades: string[] = []) {
  server.use(http.get(`${API_URL}/routes/grades`, () => HttpResponse.json(grades)));
}

describe('SearchPage', () => {
  it('debounces filter typing into a single request 250ms after the last keystroke', async () => {
    vi.useFakeTimers();
    try {
      mockGrades();
      const queries: string[] = [];
      server.use(
        http.get(`${API_URL}/routes`, ({ request }) => {
          queries.push(new URL(request.url).search);
          return HttpResponse.json([]);
        }),
      );

      renderWithProviders(<SearchPage />);
      await act(async () => {});
      expect(queries).toEqual(['']); // the initial, undebounced load

      const input = screen.getByPlaceholderText('Nombre de la vía o descripción…');
      for (const value of ['t', 'tr', 'tra']) {
        act(() => {
          fireEvent.change(input, { target: { value } });
        });
        act(() => {
          vi.advanceTimersByTime(100); // < 250ms: debounce keeps resetting
        });
      }
      expect(queries).toHaveLength(1); // still just the initial load

      act(() => {
        vi.advanceTimersByTime(250);
      });
      vi.useRealTimers();

      await waitFor(() => expect(queries).toHaveLength(2));
      expect(queries[1]).toBe('?q=tra');
    } finally {
      vi.useRealTimers();
    }
  });

  it('sends each changed filter as its own query param', async () => {
    vi.useFakeTimers();
    try {
      mockGrades(['6a', '6b']);
      const queries: string[] = [];
      server.use(
        http.get(`${API_URL}/routes`, ({ request }) => {
          queries.push(new URL(request.url).search);
          return HttpResponse.json([]);
        }),
      );

      renderWithProviders(<SearchPage />);
      await act(async () => {});
      expect(queries).toEqual(['']);

      // Grado's <select> immediately follows its <label class="field-label">Grado</label> —
      // Filters uses plain <label>/<select> pairs, not htmlFor/id, so match by DOM position.
      const gradoLabel = screen.getByText('Grado');
      const select = gradoLabel.nextElementSibling as HTMLSelectElement;
      act(() => {
        fireEvent.change(select, { target: { value: '6a' } });
      });
      act(() => {
        vi.advanceTimersByTime(250);
      });
      vi.useRealTimers();

      await waitFor(() => expect(queries).toHaveLength(2));
      expect(queries[1]).toBe('?grade=6a');
    } finally {
      vi.useRealTimers();
    }
  });

  it('invalidates and refetches the list after deleting a route, matching the ["routes"] prefix key', async () => {
    mockGrades();
    const route = makeRoute({ id: 7, name: 'Travesía del Gato' });
    let listCallCount = 0;
    let deleteCalled = false;
    server.use(
      http.get(`${API_URL}/routes`, () => {
        listCallCount++;
        return HttpResponse.json(deleteCalled ? [] : [route]);
      }),
      http.get(`${API_URL}/routes/7`, () => HttpResponse.json(makeRouteDetail({ id: 7, name: 'Travesía del Gato' }))),
      http.delete(`${API_URL}/routes/7`, () => {
        deleteCalled = true;
        return HttpResponse.json({ ok: true });
      }),
    );

    renderWithProviders(<SearchPage />, { auth: { permissions: [Permissions.routeDelete] } });

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    const deleteButton = await screen.findByRole('button', { name: 'Eliminar' });
    act(() => deleteButton.click());

    const confirmButton = await screen.findAllByRole('button', { name: 'Eliminar' });
    act(() => confirmButton[confirmButton.length - 1]!.click());

    await waitFor(() => expect(deleteCalled).toBe(true));
    await waitFor(() => expect(listCallCount).toBe(2));
    await waitFor(() => expect(screen.queryByText('Travesía del Gato')).not.toBeInTheDocument());
  });

  it('renders the backend Spanish error message when the list fails to load', async () => {
    mockGrades();
    server.use(
      http.get(`${API_URL}/routes`, () =>
        HttpResponse.json({ error: 'No se pudieron cargar las vías' }, { status: 500 }),
      ),
    );

    renderWithProviders(<SearchPage />);

    expect(await screen.findByText('No se pudieron cargar las vías')).toBeInTheDocument();
  });
});
