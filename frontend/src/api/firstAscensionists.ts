import { api } from './client';
import type { FirstAscensionistPending, FirstAscensionistProfile, FirstAscensionistSummary } from './types';

// `includeUnvalidated` opts out of the default approved-or-mine filter — refused (403)
// unless the caller holds first-ascensionist.validate. Only FirstAscensionistsPage
// (/manage/first-ascensionists) passes it.
export function listFirstAscensionists(q?: string, includeUnvalidated = false): Promise<FirstAscensionistSummary[]> {
  const params = new URLSearchParams();
  if (q) params.set('q', q);
  if (includeUnvalidated) params.set('includeUnvalidated', 'true');
  const qs = params.toString();
  return api.get<FirstAscensionistSummary[]>(`/first-ascensionists${qs ? `?${qs}` : ''}`);
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

// No encodeURIComponent — slugs are already URL-safe, same as api/areas.ts:48.
export function getFirstAscensionistBySlug(slug: string): Promise<FirstAscensionistProfile> {
  return api.get<FirstAscensionistProfile>(`/first-ascensionists/by-slug/${slug}`);
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
