import { api } from './client';
import type { TopoSummary } from './types';

export function uploadTopo(routeId: number, file: File): Promise<TopoSummary> {
  const formData = new FormData();
  formData.append('file', file);
  return api.postForm<TopoSummary>(`/routes/${routeId}/topos`, formData);
}

export function deleteTopo(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/topos/${id}`);
}
