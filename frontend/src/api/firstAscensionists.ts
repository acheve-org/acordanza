import { api } from './client';
import type { FirstAscensionistPending, FirstAscensionistProfile, FirstAscensionistSummary } from './types';

export function listFirstAscensionists(q?: string): Promise<FirstAscensionistSummary[]> {
  const qs = q ? `?q=${encodeURIComponent(q)}` : '';
  return api.get<FirstAscensionistSummary[]>(`/first-ascensionists${qs}`);
}

// The review queue's view of not-yet-decided first ascensionists.
export function listPendingFirstAscensionists(): Promise<FirstAscensionistPending[]> {
  return api.get<FirstAscensionistPending[]>('/first-ascensionists/pending');
}

// The same list enriched with each one's openings — only the "Sigue" section needs this.
export function listFirstAscensionistProfiles(q?: string): Promise<FirstAscensionistProfile[]> {
  const qs = q ? `?q=${encodeURIComponent(q)}` : '';
  return api.get<FirstAscensionistProfile[]>(`/first-ascensionists/profiles${qs}`);
}

export function getFirstAscensionist(id: number): Promise<FirstAscensionistProfile> {
  return api.get<FirstAscensionistProfile>(`/first-ascensionists/${id}`);
}

export function createFirstAscensionist(name: string): Promise<FirstAscensionistSummary> {
  return api.post<FirstAscensionistSummary>('/first-ascensionists', { name });
}

export function updateFirstAscensionist(id: number, name: string): Promise<FirstAscensionistSummary> {
  return api.put<FirstAscensionistSummary>(`/first-ascensionists/${id}`, { name });
}

export function deleteFirstAscensionist(id: number): Promise<void> {
  return api.delete<void>(`/first-ascensionists/${id}`);
}

export function validateFirstAscensionist(id: number): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/first-ascensionists/${id}/validate`);
}

export function rejectFirstAscensionist(id: number, reason: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>(`/first-ascensionists/${id}/reject`, { reason });
}
