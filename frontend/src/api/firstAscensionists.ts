import { api } from './client';
import type {
  FirstAscensionistPending,
  FirstAscensionistProfile,
  FirstAscensionistSummary,
  PagedFirstAscensionists,
} from './types';

export interface ListFirstAscensionistsParams {
  q?: string;
  // Opts out of the default approved-or-mine filter — refused (403) unless the caller
  // holds first-ascensionist.validate. Only FirstAscensionistsPage (/manage/first-ascensionists) passes it.
  includeUnvalidated?: boolean;
  // Narrows to records with no linked account — RequestFirstAscensionistLinkModal's candidate
  // list, filtered server-side so totalCount/paging reflect what's actually offered.
  unlinkedOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export function listFirstAscensionists(params: ListFirstAscensionistsParams = {}): Promise<PagedFirstAscensionists> {
  const { q, includeUnvalidated = false, unlinkedOnly = false, page = 1, pageSize = 10 } = params;
  const search = new URLSearchParams();
  if (q) search.set('q', q);
  if (includeUnvalidated) search.set('includeUnvalidated', 'true');
  if (unlinkedOnly) search.set('unlinkedOnly', 'true');
  search.set('page', String(page));
  search.set('pageSize', String(pageSize));
  return api.get<PagedFirstAscensionists>(`/first-ascensionists?${search.toString()}`);
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
