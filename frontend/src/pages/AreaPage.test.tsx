import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeAreaDetail, makeRoute, makeTopo } from '../test/factories';
import { AreaPage } from './AreaPage';

function LocationProbe({ onLocation }: { onLocation: (pathname: string, hasBackgroundLocation: boolean) => void }) {
  const location = useLocation();
  onLocation(location.pathname, Boolean((location.state as { backgroundLocation?: unknown } | null)?.backgroundLocation));
  return null;
}

describe('AreaPage', () => {
  it('clicking a route card navigates to its detail URL with the current location as backgroundLocation', async () => {
    server.use(
      http.get(`${API_URL}/areas/by-slug-path/pena-rua`, () =>
        HttpResponse.json(makeAreaDetail({ id: 1, name: 'Peña Rúa', slug: 'pena-rua' })),
      ),
      http.get(`${API_URL}/routes`, () =>
        HttpResponse.json([
          makeRoute({ id: 7, name: 'Travesía del Gato', slug: 'travesia-del-gato', areaSlugPath: 'pena-rua' }),
        ]),
      ),
    );

    let pathname = '';
    let hasBackgroundLocation = false;
    renderWithProviders(
      <Routes>
        <Route path="/areas/*" element={<AreaPage />} />
        <Route
          path="*"
          element={
            <LocationProbe
              onLocation={(p, hasBg) => {
                pathname = p;
                hasBackgroundLocation = hasBg;
              }}
            />
          }
        />
      </Routes>,
      { initialEntries: ['/areas/pena-rua'] },
    );

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    await waitFor(() => expect(pathname).toBe('/routes/pena-rua/travesia-del-gato'));
    expect(hasBackgroundLocation).toBe(true);
  });

  it('shows the not-found state for an unknown area slug path', async () => {
    server.use(
      http.get(`${API_URL}/areas/by-slug-path/no-existe`, () =>
        HttpResponse.json({ error: 'el sector no existe' }, { status: 404 }),
      ),
    );

    renderWithProviders(
      <Routes>
        <Route path="/areas/*" element={<AreaPage />} />
      </Routes>,
      { initialEntries: ['/areas/no-existe'] },
    );

    expect(await screen.findByText('Este sector no existe.')).toBeInTheDocument();
  });

  it('refetches the area after uploading a photo, showing the new media', async () => {
    let uploaded = false;
    server.use(
      http.get(`${API_URL}/areas/by-slug-path/pena-rua`, () =>
        HttpResponse.json(
          makeAreaDetail({
            id: 1,
            name: 'Peña Rúa',
            slug: 'pena-rua',
            canManageMedia: true,
            media: uploaded
              ? [makeTopo({ id: 1, url: 'https://example.com/photo.jpg', fileName: 'photo.jpg', uploadedBy: 'Hugo' })]
              : [],
          }),
        ),
      ),
      http.get(`${API_URL}/routes`, () => HttpResponse.json([])),
      http.post(`${API_URL}/areas/1/media`, () => {
        uploaded = true;
        return HttpResponse.json({
          id: 1,
          url: 'https://example.com/photo.jpg',
          fileName: 'photo.jpg',
          createdAt: '2026-01-01T00:00:00Z',
          uploadedBy: 'Hugo',
        });
      }),
    );

    renderWithProviders(
      <Routes>
        <Route path="/areas/*" element={<AreaPage />} />
      </Routes>,
      { initialEntries: ['/areas/pena-rua'] },
    );

    await waitFor(() => expect(screen.getByText('Este sector todavía no tiene fotos.')).toBeInTheDocument());

    const file = new File(['x'], 'photo.jpg', { type: 'image/jpeg' });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await act(async () => {
      fireEvent.change(input, { target: { files: [file] } });
    });

    await waitFor(() =>
      expect(screen.queryByText('Este sector todavía no tiene fotos.')).not.toBeInTheDocument(),
    );
  });
});
