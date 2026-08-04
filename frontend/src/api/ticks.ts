import { api } from './client';
import type { PagedTicks, TickFilters } from './types';

export function listMyTicks(filters: TickFilters, page: number, pageSize: number): Promise<PagedTicks> {
  const params = new URLSearchParams();
  if (filters.q) params.set('q', filters.q);
  if (filters.areaId) params.set('areaId', filters.areaId);
  if (filters.gear) params.set('gear', filters.gear);
  if (filters.tag) params.set('tag', filters.tag);
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));
  return api.get<PagedTicks>(`/ticks/mine?${params.toString()}`);
}
