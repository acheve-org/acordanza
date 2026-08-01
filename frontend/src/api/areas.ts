import { api } from './client';
import type { AreaSummary, GeoPoint } from './types';

export function listAreas(): Promise<AreaSummary[]> {
  return api.get<AreaSummary[]>('/areas');
}

export function createArea(name: string, parentAreaId: number | null): Promise<AreaSummary> {
  return api.post<AreaSummary>('/areas', { name, parentAreaId });
}

export function updateAreaCoordinates(id: number, polygon: GeoPoint[]): Promise<AreaSummary> {
  return api.put<AreaSummary>(`/areas/${id}/coordinates`, { polygon });
}
