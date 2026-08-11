import type { ContributionKind } from './contributions';
import type { RouteFilters } from './types';

// One place for react-query key shapes, so pages invalidate/match against
// the same structure they queried with. Add an entry here as each page
// converts rather than inlining key arrays at the call site.
export const queryKeys = {
  routes: (filters: RouteFilters) => ['routes', filters] as const,
  contributions: (kind: ContributionKind, page: number) => ['contributions', kind, page] as const,
};
