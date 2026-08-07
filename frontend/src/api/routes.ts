import { api } from './client';
import type {
  CreateTickInput,
  RouteDetail,
  RouteFilters,
  RouteSummary,
  UpsertRouteInput,
} from './types';

// `exactArea` restricts results to exactly `areaId`, skipping the descendant-area
// expansion every other caller (filters, area picker) relies on — used by the area
// detail page, whose child areas already get their own routes section.
export function listRoutes(filters: RouteFilters, exactArea?: boolean): Promise<RouteSummary[]> {
  const params = new URLSearchParams();
  if (filters.q) params.set('q', filters.q);
  if (filters.areaId) params.set('areaId', filters.areaId);
  if (filters.gear) params.set('gear', filters.gear);
  if (filters.tag) params.set('tag', filters.tag);
  if (filters.hasTopo) params.set('hasTopo', filters.hasTopo);
  if (exactArea) params.set('exactArea', 'true');
  const qs = params.toString();
  return api.get<RouteSummary[]>(`/routes${qs ? `?${qs}` : ''}`);
}

export function getRoute(id: number): Promise<RouteDetail> {
  return api.get<RouteDetail>(`/routes/${id}`);
}

export function createRoute(input: UpsertRouteInput): Promise<{ id: number }> {
  return api.post<{ id: number }>('/routes', input);
}

export function updateRoute(id: number, input: UpsertRouteInput): Promise<{ ok: true }> {
  return api.put<{ ok: true }>(`/routes/${id}`, input);
}

export function deleteRoute(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/routes/${id}`);
}

export function validateRoute(id: number): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/routes/${id}/validate`);
}

export function createTick(routeId: number, input: CreateTickInput): Promise<{ id: number }> {
  return api.post<{ id: number }>(`/routes/${routeId}/ticks`, input);
}

export function deleteTick(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/ticks/${id}`);
}
