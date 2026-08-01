import { api } from './client';
import type { FirstAscensionistSummary } from './types';

export function listFirstAscensionists(q?: string): Promise<FirstAscensionistSummary[]> {
  const qs = q ? `?q=${encodeURIComponent(q)}` : '';
  return api.get<FirstAscensionistSummary[]>(`/first-ascensionists${qs}`);
}

export function createFirstAscensionist(name: string): Promise<FirstAscensionistSummary> {
  return api.post<FirstAscensionistSummary>('/first-ascensionists', { name });
}
