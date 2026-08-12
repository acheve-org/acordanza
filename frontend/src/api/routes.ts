import { api } from './client';
import type {
  CreateTickInput,
  RouteDetail,
  RouteFilters,
  RoutePending,
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
  if (filters.grade) params.set('grade', filters.grade);
  for (const o of filters.orientation) params.append('orientation', o);
  if (filters.gear) params.set('gear', filters.gear);
  if (filters.hasTopo) params.set('hasTopo', filters.hasTopo);
  if (exactArea) params.set('exactArea', 'true');
  const qs = params.toString();
  return api.get<RouteSummary[]>(`/routes${qs ? `?${qs}` : ''}`);
}

// Routes opened by one first ascensionist — the "Sigue" section's per-aperturista list.
export function listRoutesByFirstAscensionist(firstAscensionistId: number): Promise<RouteSummary[]> {
  return api.get<RouteSummary[]>(`/routes?firstAscensionistId=${firstAscensionistId}`);
}

// "Mis aperturas": routes the caller opened as a first ascensionist, regardless of
// validation status — server-side filtered and never gated by visibility (see
// RoutesController.GetRoutes' openedByMe branch), so no whole-table download + client
// `.filter()` is needed here.
export function listMyOpenings(): Promise<RouteSummary[]> {
  return api.get<RouteSummary[]>('/routes?openedByMe=true');
}

// The grades actually in use, for the search filter's dropdown.
export function listGrades(): Promise<string[]> {
  return api.get<string[]>('/routes/grades');
}

// The review queue's view of not-yet-decided routes — narrowed server-side to the ones
// the caller can actually validate (role or area-expert), see RoutesController.GetPendingRoutes.
export function listPendingRoutes(): Promise<RoutePending[]> {
  return api.get<RoutePending[]>('/routes/pending');
}

export function getRoute(id: number): Promise<RouteDetail> {
  return api.get<RouteDetail>(`/routes/${id}`);
}

// Resolves a route by its full shareable path — {area-slug-path}/{route-slug} — the
// data source for RouteDetailOverlay, the sole place a route's detail is ever fetched
// from now on. `path` is not URL-encoded segment by segment since it already comes
// from a route param carrying literal "/"-joined slugs, not user input.
export function getRouteBySlugPath(path: string): Promise<RouteDetail> {
  return api.get<RouteDetail>(`/routes/by-slug-path/${path}`);
}

// The shareable detail URL for a route, built client-side from its area slug path +
// own slug — every list card/row carries both fields precisely so callers can build
// this without a second round-trip. Shared by every page that opens the overlay.
export function routeDetailPath(route: { areaSlugPath: string; slug: string }): string {
  return `/routes/${route.areaSlugPath}/${route.slug}`;
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

export function rejectRoute(id: number, reason: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/routes/${id}/reject`, { reason });
}

// Kicks off the enrichment worker (plan 18) for one route — async, no polling; the
// caller reloads the route detail a few minutes later to see the results.
export function enrichRoute(id: number, force = false): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/routes/${id}/enrich${force ? '?force=true' : ''}`);
}

export function createTick(routeId: number, input: CreateTickInput): Promise<{ id: number }> {
  return api.post<{ id: number }>(`/routes/${routeId}/ticks`, input);
}

export function deleteTick(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/ticks/${id}`);
}
