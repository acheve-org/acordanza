import { queryKeys } from './queryKeys';
import { emptyRouteFilters } from './types';

describe('queryKeys', () => {
  it('routes() key stays prefix-matchable by the bare ["routes"] key', () => {
    const key = queryKeys.routes(emptyRouteFilters);

    // SearchPage's invalidateRouteQueries invalidates with ['routes'] and relies on
    // react-query's default prefix matching against this key — it must start the same way.
    expect(key[0]).toBe('routes');
    expect(key).toEqual(['routes', emptyRouteFilters]);
  });

  it('route(id) key embeds the id', () => {
    expect(queryKeys.route(42)).toEqual(['route', 42]);
  });
});
