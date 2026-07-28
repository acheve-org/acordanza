import { api } from './client';
import type {
  CreateTickInput,
  RouteDetail,
  RouteFilters,
  RouteSummary,
  UpsertRouteInput,
} from './types';

export function listRoutes(filters: RouteFilters): Promise<RouteSummary[]> {
  const params = new URLSearchParams();
  if (filters.q) params.set('q', filters.q);
  if (filters.cragId) params.set('cragId', filters.cragId);
  if (filters.gear) params.set('gear', filters.gear);
  if (filters.tag) params.set('tag', filters.tag);
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

export function createTick(routeId: number, input: CreateTickInput): Promise<{ id: number }> {
  return api.post<{ id: number }>(`/routes/${routeId}/ticks`, input);
}

export function deleteTick(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/ticks/${id}`);
}
