import { api } from './client';
import type { FirstAscensionistSummary, UserSummary } from './types';

export function listFirstAscensionists(): Promise<FirstAscensionistSummary[]> {
  return api.get<FirstAscensionistSummary[]>('/first-ascensionists');
}

export function createFirstAscensionist(name: string, userId: string | null): Promise<FirstAscensionistSummary> {
  return api.post<FirstAscensionistSummary>('/first-ascensionists', { name, userId });
}

export function listLinkableUsers(): Promise<UserSummary[]> {
  return api.get<UserSummary[]>('/first-ascensionists/users');
}
