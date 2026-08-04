import { api } from './client';
import type { AreaBounds, AreaNode, AreaSummary, GeoPoint, PagedAreaNodes } from './types';

export function listRootAreas(page: number, pageSize = 10): Promise<PagedAreaNodes> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return api.get<PagedAreaNodes>(`/areas/roots?${params}`);
}

export function listAreaChildren(id: number): Promise<AreaNode[]> {
  return api.get<AreaNode[]>(`/areas/${id}/children`);
}

export function listAreas(bounds?: AreaBounds): Promise<AreaSummary[]> {
  if (!bounds) return api.get<AreaSummary[]>('/areas');
  const params = new URLSearchParams({
    minLat: String(bounds.minLat),
    minLng: String(bounds.minLng),
    maxLat: String(bounds.maxLat),
    maxLng: String(bounds.maxLng),
  });
  return api.get<AreaSummary[]>(`/areas?${params}`);
}

export function createArea(name: string, parentAreaId: number | null): Promise<AreaSummary> {
  return api.post<AreaSummary>('/areas', { name, parentAreaId });
}

export function updateArea(id: number, name: string, parentAreaId: number | null): Promise<AreaSummary> {
  return api.put<AreaSummary>(`/areas/${id}`, { name, parentAreaId });
}

export function deleteArea(id: number): Promise<void> {
  return api.delete<void>(`/areas/${id}`);
}

export function updateAreaCoordinates(id: number, polygon: GeoPoint[]): Promise<AreaSummary> {
  return api.put<AreaSummary>(`/areas/${id}/coordinates`, { polygon });
}

export function validateArea(id: number): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/areas/${id}/validate`);
}

// Builds the "Padre › Hijo › Nieto" breadcrumb for an area given a lookup of all areas by id.
export function areaPath(area: AreaSummary, byId: Map<number, AreaSummary>): string {
  const parts: string[] = [];
  let current: AreaSummary | undefined = area;
  while (current) {
    parts.unshift(current.name);
    current = current.parentAreaId !== null ? byId.get(current.parentAreaId) : undefined;
  }
  return parts.join(' › ');
}
