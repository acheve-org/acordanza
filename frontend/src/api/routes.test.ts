import { listRoutes } from './routes';
import { emptyRouteFilters } from './types';
import { api } from './client';

vi.mock('./client', () => ({
  api: { get: vi.fn().mockResolvedValue([]) },
}));

beforeEach(() => {
  vi.mocked(api.get).mockClear();
});

describe('listRoutes', () => {
  it('requests a bare /routes with no query string when filters are empty', async () => {
    await listRoutes(emptyRouteFilters);

    expect(api.get).toHaveBeenCalledWith('/routes');
  });

  it('maps each filter to its own query param', async () => {
    await listRoutes({
      q: 'travesía',
      areaId: '3',
      grade: '6a',
      orientation: ['north'],
      gear: 'bolted',
      hasTopo: 'true',
    });

    const [path] = vi.mocked(api.get).mock.calls[0]!;
    const params = new URLSearchParams(path.split('?')[1]);
    expect(params.get('q')).toBe('travesía');
    expect(params.get('areaId')).toBe('3');
    expect(params.get('grade')).toBe('6a');
    expect(params.get('orientation')).toBe('north');
    expect(params.get('gear')).toBe('bolted');
    expect(params.get('hasTopo')).toBe('true');
  });

  it('omits falsy filter values', async () => {
    await listRoutes({ ...emptyRouteFilters, q: 'x', areaId: '', grade: '' });

    expect(api.get).toHaveBeenCalledWith('/routes?q=x');
  });

  it('serialises each selected orientation as a repeated query param', async () => {
    await listRoutes({ ...emptyRouteFilters, orientation: ['north', 'south'] });

    expect(api.get).toHaveBeenCalledWith('/routes?orientation=north&orientation=south');
  });

  it('omits the orientation parameter entirely when empty', async () => {
    await listRoutes({ ...emptyRouteFilters, orientation: [] });

    expect(api.get).toHaveBeenCalledWith('/routes');
  });

  it('only adds exactArea when truthy', async () => {
    await listRoutes(emptyRouteFilters, true);
    expect(api.get).toHaveBeenCalledWith('/routes?exactArea=true');

    vi.mocked(api.get).mockClear();
    await listRoutes(emptyRouteFilters, false);
    expect(api.get).toHaveBeenCalledWith('/routes');
  });
});
