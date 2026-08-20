import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeFirstAscensionist, makePagedFirstAscensionists } from '../test/factories';
import { FirstAscensionistPicker } from './FirstAscensionistPicker';

describe('FirstAscensionistPicker deferred fetching', () => {
  it('does not fetch /first-ascensionists on mount with nothing selected', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () => {
        calls++;
        return HttpResponse.json(makePagedFirstAscensionists());
      }),
    );

    renderWithProviders(<FirstAscensionistPicker value={[]} onChange={() => {}} />);
    await act(async () => {});

    expect(calls).toBe(0);
  });

  it('fetches /first-ascensionists once the combobox opens', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () => {
        calls++;
        return HttpResponse.json(
          makePagedFirstAscensionists({ items: [makeFirstAscensionist({ id: 1, name: 'Alex Aperturista' })] }),
        );
      }),
    );

    renderWithProviders(<FirstAscensionistPicker value={[]} onChange={() => {}} />);
    fireEvent.focus(screen.getByPlaceholderText('Buscar aperturista…'));

    await waitFor(() => expect(calls).toBe(1));
    expect(await screen.findByRole('button', { name: 'Alex Aperturista' })).toBeInTheDocument();
  });

  it('fetches on mount when a selection is already present, to resolve its chip label', async () => {
    let calls = 0;
    server.use(
      http.get(`${API_URL}/first-ascensionists`, () => {
        calls++;
        return HttpResponse.json(
          makePagedFirstAscensionists({ items: [makeFirstAscensionist({ id: 1, name: 'Alex Aperturista' })] }),
        );
      }),
    );

    renderWithProviders(<FirstAscensionistPicker value={[1]} onChange={() => {}} />);

    await waitFor(() => expect(calls).toBe(1));
  });
});
