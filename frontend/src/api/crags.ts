import { api } from './client';
import type { CragSummary } from './types';

export function listCrags(): Promise<CragSummary[]> {
  return api.get<CragSummary[]>('/crags');
}

export function createCrag(name: string, parentCragId: number | null): Promise<CragSummary> {
  return api.post<CragSummary>('/crags', { name, parentCragId });
}
