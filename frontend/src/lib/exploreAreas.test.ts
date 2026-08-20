import { boundsOfPolygon, deriveVisibleAreas, sortByDistance, SUBAREA_ZOOM } from './exploreAreas';
import type { AreaSummary } from '../api/types';

function makeArea(o: Partial<AreaSummary> & { id: number; name: string }): AreaSummary {
  return {
    parentAreaId: null,
    slug: `area-${o.id}`,
    centroidLat: null,
    centroidLng: null,
    polygon: [],
    validationStatus: 'approved',
    ...o,
  } as AreaSummary;
}

describe('deriveVisibleAreas', () => {
  const parent = makeArea({ id: 1, name: 'Peña Rúa', centroidLat: 42, centroidLng: -3 });
  const child = makeArea({ id: 2, name: 'Sector Norte', parentAreaId: 1, centroidLat: 42.01, centroidLng: -3.01 });
  const otherParent = makeArea({ id: 3, name: 'Sasamón', centroidLat: 43, centroidLng: -4 });
  const areas = [parent, child, otherParent];

  it('below SUBAREA_ZOOM only groups top-level areas, with no active area', () => {
    const result = deriveVisibleAreas(areas, { lat: 42, lng: -3 }, SUBAREA_ZOOM - 1);

    expect(result.topLevelAreas.map((a) => a.id)).toEqual([1, 3]);
    expect(result.activeTopLevelArea).toBeNull();
    expect(result.subareas).toEqual([]);
  });

  it('at or above SUBAREA_ZOOM picks the closest top-level area and reveals its subareas', () => {
    const result = deriveVisibleAreas(areas, { lat: 42, lng: -3 }, SUBAREA_ZOOM);

    expect(result.activeTopLevelArea?.id).toBe(1);
    expect(result.subareas.map((a) => a.id)).toEqual([2]);
  });

  it('has no active area when center is null even above SUBAREA_ZOOM', () => {
    const result = deriveVisibleAreas(areas, null, SUBAREA_ZOOM);

    expect(result.activeTopLevelArea).toBeNull();
    expect(result.subareas).toEqual([]);
  });
});

describe('sortByDistance', () => {
  it('orders by distance to center, areas with no centroid sort last', () => {
    const near = makeArea({ id: 1, name: 'Near', centroidLat: 42.01, centroidLng: -3.01 });
    const far = makeArea({ id: 2, name: 'Far', centroidLat: 45, centroidLng: -6 });
    const noCentroid = makeArea({ id: 3, name: 'No centroid' });

    const result = sortByDistance([far, noCentroid, near], { lat: 42, lng: -3 });

    expect(result.map((a) => a.id)).toEqual([1, 2, 3]);
  });
});

describe('boundsOfPolygon', () => {
  it('returns the min/max lat/lng of the polygon vertices', () => {
    const bounds = boundsOfPolygon([
      { lat: 42, lng: -3 },
      { lat: 42.5, lng: -3.2 },
      { lat: 41.8, lng: -2.9 },
    ]);

    expect(bounds).toEqual([
      [41.8, -3.2],
      [42.5, -2.9],
    ]);
  });
});
