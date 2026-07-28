import { api } from './client';
import type { RouteOpenerSummary, UserSummary } from './types';

export function listOpeners(): Promise<RouteOpenerSummary[]> {
  return api.get<RouteOpenerSummary[]>('/route-openers');
}

export function createOpener(name: string, userId: string | null): Promise<RouteOpenerSummary> {
  return api.post<RouteOpenerSummary>('/route-openers', { name, userId });
}

export function listLinkableUsers(): Promise<UserSummary[]> {
  return api.get<UserSummary[]>('/route-openers/users');
}
