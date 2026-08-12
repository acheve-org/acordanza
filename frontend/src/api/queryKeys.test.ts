import { orientations } from 'acordanza-design-system';
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

  it('two differently-ordered orientation selections normalise to the same key', () => {
    // CompassPicker always rebuilds its array in canonical compass order (see its
    // onChange), so picking N then S produces the same filter state as picking S then
    // N. react-query hashes ['routes', filters] structurally, so unless the caller
    // normalises the order itself, the two click sequences would land in different
    // cache entries and fire two identical requests.
    const pickedNorthThenSouth = orientations.filter((o) => (['south', 'north'] as string[]).includes(o));
    const pickedSouthThenNorth = orientations.filter((o) => (['north', 'south'] as string[]).includes(o));

    const a = { ...emptyRouteFilters, orientation: pickedNorthThenSouth };
    const b = { ...emptyRouteFilters, orientation: pickedSouthThenNorth };

    expect(a.orientation).toEqual(b.orientation);
    expect(queryKeys.routes(a)).toEqual(queryKeys.routes(b));
  });
});
