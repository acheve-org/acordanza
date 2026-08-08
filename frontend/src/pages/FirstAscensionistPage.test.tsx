import { screen, waitFor } from '@testing-library/react';
import { Route, Routes } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeFirstAscensionistProfile, makeRoute } from '../test/factories';
import { FirstAscensionistPage } from './FirstAscensionistPage';

describe('FirstAscensionistPage', () => {
  it('renders the aperturista resolved by slug and fetches their routes with the id from the profile', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists/by-slug/hugo-biarge`, () =>
        HttpResponse.json(
          makeFirstAscensionistProfile({ id: 42, name: 'Hugo Biarge', slug: 'hugo-biarge', routeCount: 1 }),
        ),
      ),
      http.get(`${API_URL}/routes`, ({ request }) => {
        expect(new URL(request.url).search).toBe('?firstAscensionistId=42');
        return HttpResponse.json([makeRoute({ id: 7, name: 'Travesía del Gato' })]);
      }),
    );

    renderWithProviders(
      <Routes>
        <Route path="/fa/:slug" element={<FirstAscensionistPage />} />
      </Routes>,
      { initialEntries: ['/fa/hugo-biarge'] },
    );

    expect(await screen.findByRole('heading', { name: 'Hugo Biarge' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('Travesía del Gato')).toBeInTheDocument());
  });

  it('shows the not-found state for an unknown slug', async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists/by-slug/nadie`, () =>
        HttpResponse.json({ error: 'el aperturista no existe' }, { status: 404 }),
      ),
    );

    renderWithProviders(
      <Routes>
        <Route path="/fa/:slug" element={<FirstAscensionistPage />} />
      </Routes>,
      { initialEntries: ['/fa/nadie'] },
    );

    expect(await screen.findByText('Este aperturista no existe.')).toBeInTheDocument();
  });
});
