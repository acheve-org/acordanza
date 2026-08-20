import { useEffect } from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeArea } from '../test/factories';
import { ExplorePage } from './ExplorePage';
import type { AreaMapProps } from '../components/AreaMap';

// Leaflet rendering is out of scope for this layer (frontend/CLAUDE.md) — stub AreaMap so
// ExplorePage's own wiring (props in, fly-target/selection state out) can be asserted
// without a real map. It fires onBoundsChange once on mount, the same way the real
// BoundsTracker reports the map's initial viewport.
let lastProps: AreaMapProps;
let mockZoom = 8;
vi.mock('../components/AreaMap', () => ({
  AreaMap: (props: AreaMapProps) => {
    lastProps = props;
    useEffect(() => {
      props.onBoundsChange({ minLat: 40, minLng: -4, maxLat: 41, maxLng: -3 }, mockZoom);
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);
    return <div data-testid="area-map-stub" />;
  },
}));

beforeEach(() => {
  mockZoom = 8;
});

describe('ExplorePage', () => {
  it('renders the list from the viewport-bounded GET /areas response', async () => {
    server.use(
      http.get(`${API_URL}/areas`, () => HttpResponse.json([makeArea({ id: 1, name: 'Peña Rúa' })])),
    );

    renderWithProviders(<ExplorePage />);

    expect(await screen.findByText('Peña Rúa')).toBeInTheDocument();
  });

  it('shows the empty state when the viewport has no areas', async () => {
    server.use(http.get(`${API_URL}/areas`, () => HttpResponse.json([])));

    renderWithProviders(<ExplorePage />);

    expect(await screen.findByText('No hay zonas en esta vista — aleja el mapa o desplázate.')).toBeInTheDocument();
  });

  it('selecting a row flies the map to that area', async () => {
    const user = userEvent.setup();
    server.use(
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([
          makeArea({ id: 1, name: 'Peña Rúa', centroidLat: 42.5, centroidLng: -3.1 }),
        ]),
      ),
    );

    renderWithProviders(<ExplorePage />);

    await user.click(await screen.findByText('Peña Rúa'));

    expect(lastProps.selectedAreaId).toBe(1);
    expect(lastProps.flyTarget).toEqual({ center: [42.5, -3.1], zoom: 12 });
  });

  it('shows a distinct depth icon per row and no "Con/Sin sectores" text', async () => {
    mockZoom = 12;
    server.use(
      http.get(`${API_URL}/areas`, () =>
        HttpResponse.json([
          makeArea({ id: 1, name: 'Peña Rúa', centroidLat: 40.5, centroidLng: -3.5 }),
          makeArea({ id: 2, name: 'Sector Sur', parentAreaId: 1 }),
        ]),
      ),
    );

    renderWithProviders(<ExplorePage />);

    const rootRow = (await screen.findByText('Peña Rúa', { selector: '.list-row-title' })).closest('li');
    const subareaRow = (await screen.findByText('Sector Sur', { selector: '.list-row-title' })).closest('li');

    const rootIcon = rootRow?.querySelector('.list-row-leading svg');
    const subareaIcon = subareaRow?.querySelector('.list-row-leading svg');

    expect(rootIcon?.getAttribute('style')).toContain('--depth-root');
    expect(subareaIcon?.getAttribute('style')).toContain('--depth-subarea');
    expect(rootIcon?.innerHTML).not.toBe(subareaIcon?.innerHTML);

    expect(screen.queryByText('Con sectores')).not.toBeInTheDocument();
    expect(screen.queryByText('Sin sectores')).not.toBeInTheDocument();
  });
});
