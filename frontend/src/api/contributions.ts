import { api } from './client';
import type { PagedContributions } from './types';

// The three endpoints are shape-identical and the only caller picks between them from tab
// state, so one parameterised wrapper rather than three identical bodies plus a lookup.
export type ContributionKind = 'routes' | 'areas' | 'first-ascensionists';

export function listMyContributions(
  kind: ContributionKind,
  page: number,
  pageSize = 10,
): Promise<PagedContributions> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return api.get<PagedContributions>(`/contributions/${kind}?${params}`);
}
