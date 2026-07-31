import { api } from './client';
import type { InviteUserInput, PagedUsers, UserFilters } from './types';

const PAGE_SIZE = 10;

export function listUsers(filters: UserFilters): Promise<PagedUsers> {
  const params = new URLSearchParams();
  if (filters.q) params.set('q', filters.q);
  if (filters.role) params.set('role', filters.role);
  params.set('page', String(filters.page));
  params.set('pageSize', String(PAGE_SIZE));
  params.set('sortBy', filters.sortBy);
  params.set('sortDir', filters.sortDir);
  return api.get<PagedUsers>(`/users?${params.toString()}`);
}

export function inviteUser(input: InviteUserInput): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/users/invite', input);
}
