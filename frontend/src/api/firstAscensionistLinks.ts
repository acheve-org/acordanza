import { api } from './client';
import type { FirstAscensionistLinkRequestSummary } from './types';

export function listMyFirstAscensionistLinks(): Promise<FirstAscensionistLinkRequestSummary[]> {
  return api.get<FirstAscensionistLinkRequestSummary[]>('/first-ascensionist-links/mine');
}

export function listPendingFirstAscensionistLinks(): Promise<FirstAscensionistLinkRequestSummary[]> {
  return api.get<FirstAscensionistLinkRequestSummary[]>('/first-ascensionist-links/pending');
}

export function requestFirstAscensionistLink(firstAscensionistId: number): Promise<FirstAscensionistLinkRequestSummary> {
  return api.post<FirstAscensionistLinkRequestSummary>('/first-ascensionist-links', { firstAscensionistId });
}

export function approveFirstAscensionistLink(id: number): Promise<FirstAscensionistLinkRequestSummary> {
  return api.post<FirstAscensionistLinkRequestSummary>(`/first-ascensionist-links/${id}/approve`);
}

export function rejectFirstAscensionistLink(id: number, reason: string): Promise<FirstAscensionistLinkRequestSummary> {
  return api.post<FirstAscensionistLinkRequestSummary>(`/first-ascensionist-links/${id}/reject`, { reason });
}

export function cancelFirstAscensionistLink(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/first-ascensionist-links/${id}`);
}
