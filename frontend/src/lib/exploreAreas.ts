import type L from 'leaflet';
import type { AreaSummary, GeoPoint } from '../api/types';

// Below this zoom, only top-level area markers are shown; at or above it, the
// nearest top-level area's subareas are revealed as polygons. Kept in sync
// with AreaMap's own SUBAREA_ZOOM (both need the same constant, not an import
// of the other — AreaMap is React.lazy and this module must stay framework-free).
export const SUBAREA_ZOOM = 12;

// Roughly the "~100km around" zoom level Web Mercator tiles give at this scale — between
// SPAIN_ZOOM (6, country-scale, AreaMap.tsx) and SUBAREA_ZOOM (12, one crag's sectors).
// Approximate by design, not a computed radius.
export const DEFAULT_LOCATION_ZOOM = 9;

function centroidOf(area: AreaSummary): GeoPoint | null {
  return area.centroidLat !== null && area.centroidLng !== null
    ? { lat: area.centroidLat, lng: area.centroidLng }
    : null;
}

export interface VisibleAreas {
  topLevelAreas: AreaSummary[];
  activeTopLevelArea: AreaSummary | null;
  subareas: AreaSummary[];
}

export function deriveVisibleAreas(
  areas: AreaSummary[],
  center: L.LatLngLiteral | null,
  zoom: number,
): VisibleAreas {
  const topLevelAreas = areas.filter((a) => a.parentAreaId === null);

  let activeTopLevelArea: AreaSummary | null = null;
  if (center && zoom >= SUBAREA_ZOOM) {
    let bestDistance = Infinity;
    for (const area of topLevelAreas) {
      const centroid = centroidOf(area);
      if (!centroid) continue;
      const distance = Math.hypot(centroid.lat - center.lat, centroid.lng - center.lng);
      if (distance < bestDistance) {
        bestDistance = distance;
        activeTopLevelArea = area;
      }
    }
  }

  const subareas = activeTopLevelArea
    ? areas.filter((a) => a.parentAreaId === activeTopLevelArea!.id)
    : [];

  return { topLevelAreas, activeTopLevelArea, subareas };
}

export function sortByDistance<T extends { centroidLat: number | null; centroidLng: number | null }>(
  areas: T[],
  center: L.LatLngLiteral | null,
): T[] {
  return [...areas].sort((a, b) => {
    const da = distanceTo(a, center);
    const db = distanceTo(b, center);
    if (da === null && db === null) return 0;
    if (da === null) return 1;
    if (db === null) return -1;
    return da - db;
  });
}

function distanceTo(
  area: { centroidLat: number | null; centroidLng: number | null },
  center: L.LatLngLiteral | null,
): number | null {
  if (!center || area.centroidLat === null || area.centroidLng === null) return null;
  return Math.hypot(area.centroidLat - center.lat, area.centroidLng - center.lng);
}

export function boundsOfPolygon(polygon: GeoPoint[]): [[number, number], [number, number]] {
  const lats = polygon.map((p) => p.lat);
  const lngs = polygon.map((p) => p.lng);
  return [
    [Math.min(...lats), Math.min(...lngs)],
    [Math.max(...lats), Math.max(...lngs)],
  ];
}
