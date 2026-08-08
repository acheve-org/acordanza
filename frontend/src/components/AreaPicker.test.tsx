import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea } from '../test/factories';
import { AreaPicker } from './AreaPicker';

// Pins frontend/CLAUDE.md's "modal/dropdown data is fetched by the thing that needs it, when
// it opens" rule for AreaPicker specifically — with `onUnhandledRequest: 'error'` (see
// src/test/setup.ts), an accidental mount-time prefetch would fail these tests outright.
describe('AreaPicker deferred fetching', () => {
  it('does not fetch /areas on mount with nothing selected', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/areas`, () => {
        calls++;
        return HttpResponse.json([]);
      }),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} />);
    await act(async () => {});

    expect(calls).toBe(0);
  });

  it('fetches /areas once the combobox opens', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/areas`, () => {
        calls++;
        return HttpResponse.json([makeArea({ id: 1, name: 'Peña Rúa', validationStatus: 'approved' })]);
      }),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} />);
    fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));

    await waitFor(() => expect(calls).toBe(1));
    expect(await screen.findByRole('button', { name: 'Peña Rúa' })).toBeInTheDocument();
  });

  it('flags a non-approved option with its validation status, since the list is approved-or-mine', async () => {
    server.use(
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([makeArea({ id: 1, name: 'Peña Rúa', validationStatus: 'pending' })]),
      ),
    );

    renderWithProviders(<AreaPicker value={null} onChange={() => {}} />);
    fireEvent.focus(screen.getByPlaceholderText('Buscar sector…'));

    expect(await screen.findByRole('button', { name: 'Peña Rúa (Pendiente de validación)' })).toBeInTheDocument();
  });

  it('fetches /areas on mount when a value is already selected, to resolve its label', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/areas`, () => {
        calls++;
        return HttpResponse.json([makeArea({ id: 1, name: 'Peña Rúa' })]);
      }),
    );

    renderWithProviders(<AreaPicker value={1} onChange={() => {}} />);

    await waitFor(() => expect(calls).toBe(1));
  });
});
