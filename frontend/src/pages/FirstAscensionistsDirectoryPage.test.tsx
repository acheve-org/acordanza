import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeFirstAscensionistProfile } from '../test/factories';
import { FirstAscensionistsDirectoryPage } from './FirstAscensionistsDirectoryPage';

describe('FirstAscensionistsDirectoryPage', () => {
  it("links each aperturista's card to /fa/{slug}", async () => {
    server.use(
      http.get(`${API_URL}/first-ascensionists/profiles`, () =>
        HttpResponse.json([
          makeFirstAscensionistProfile({ id: 1, name: 'Hugo Biarge', slug: 'hugo-biarge' }),
          makeFirstAscensionistProfile({ id: 2, name: 'José García', slug: 'jose-garcia-2' }),
        ]),
      ),
    );

    renderWithProviders(<FirstAscensionistsDirectoryPage />);

    const hugoCard = await screen.findByText('Hugo Biarge');
    expect(hugoCard.closest('a')).toHaveAttribute('href', '/fa/hugo-biarge');

    const joseCard = screen.getByText('José García');
    expect(joseCard.closest('a')).toHaveAttribute('href', '/fa/jose-garcia-2');
  });
});
