import { api } from './client';
import type { AreaExpert } from './types';

export function listMyAreaExperts(): Promise<AreaExpert[]> {
  return api.get<AreaExpert[]>('/area-experts/mine');
}

export function listPendingAreaExperts(): Promise<AreaExpert[]> {
  return api.get<AreaExpert[]>('/area-experts/pending');
}

export function requestAreaExpert(areaId: number): Promise<AreaExpert> {
  return api.post<AreaExpert>('/area-experts', { areaId });
}

export function approveAreaExpert(id: number): Promise<AreaExpert> {
  return api.post<AreaExpert>(`/area-experts/${id}/approve`);
}

export function deleteAreaExpert(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/area-experts/${id}`);
}
