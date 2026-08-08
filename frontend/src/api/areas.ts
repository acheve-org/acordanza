import { api } from './client';
import type {
  AreaBounds,
  AreaDetail,
  AreaNode,
  AreaPending,
  AreaSummary,
  GeoPoint,
  PagedAreaNodes,
  TopoSummary,
} from './types';

// `includeUnvalidated` opts out of the default approved-or-mine filter — refused (403)
// unless the caller holds area.validate. Only the management screens (AreaTree,
// AreaPolygonModal) pass it; everyone else gets the safe, filtered list by default.
export function listRootAreas(page: number, pageSize = 10, includeUnvalidated = false): Promise<PagedAreaNodes> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (includeUnvalidated) params.set('includeUnvalidated', 'true');
  return api.get<PagedAreaNodes>(`/areas/roots?${params}`);
}

export function listAreaChildren(id: number, includeUnvalidated = false): Promise<AreaNode[]> {
  const qs = includeUnvalidated ? '?includeUnvalidated=true' : '';
  return api.get<AreaNode[]>(`/areas/${id}/children${qs}`);
}

export function listAreas(bounds?: AreaBounds, includeUnvalidated = false): Promise<AreaSummary[]> {
  const params = new URLSearchParams();
  if (bounds) {
    params.set('minLat', String(bounds.minLat));
    params.set('minLng', String(bounds.minLng));
    params.set('maxLat', String(bounds.maxLat));
    params.set('maxLng', String(bounds.maxLng));
  }
  if (includeUnvalidated) params.set('includeUnvalidated', 'true');
  const qs = params.toString();
  return api.get<AreaSummary[]>(`/areas${qs ? `?${qs}` : ''}`);
}

// The review queue's view of not-yet-decided areas — GET /areas/pending.
export function listPendingAreas(): Promise<AreaPending[]> {
  return api.get<AreaPending[]>('/areas/pending');
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

export function rejectArea(id: number, reason: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/areas/${id}/reject`, { reason });
}

// `path` is the area's full slug path (e.g. "pena-rua/sector-1") — see
// AreasController.GetAreaBySlugPath. Slugs are already URL-safe, no encoding needed.
export function getAreaBySlugPath(path: string): Promise<AreaDetail> {
  return api.get<AreaDetail>(`/areas/by-slug-path/${path}`);
}

export function uploadAreaMedia(areaId: number, file: File): Promise<TopoSummary> {
  const formData = new FormData();
  formData.append('file', file);
  return api.postForm<TopoSummary>(`/areas/${areaId}/media`, formData);
}

export function deleteAreaMedia(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/area-media/${id}`);
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
