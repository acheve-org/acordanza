import type { ContributionKind } from './contributions';
import type { RouteFilters, TickFilters, UserFilters } from './types';

// One place for react-query key shapes, so pages invalidate/match against
// the same structure they queried with. Add an entry here as each page
// converts rather than inlining key arrays at the call site.
export const queryKeys = {
  routes: (filters: RouteFilters) => ['routes', filters] as const,
  contributions: (kind: ContributionKind, page: number) => ['contributions', kind, page] as const,
  users: (filters: UserFilters) => ['users', filters] as const,
  firstAscensionists: (q: string) => ['first-ascensionists', q] as const,
  firstAscensionistProfiles: (q: string) => ['first-ascensionists', 'profiles', q] as const,
  myTicks: (filters: TickFilters) => ['ticks', 'mine', filters] as const,
  rootAreas: (page: number) => ['areas', 'roots', page] as const,
  myInvites: () => ['users', 'my-invites'] as const,
  myRouteImports: () => ['route-imports', 'mine'] as const,
  myAreaExperts: () => ['area-experts', 'mine'] as const,
  pendingAreaExperts: () => ['area-experts', 'pending'] as const,
  myFirstAscensionistLinks: () => ['first-ascensionist-links', 'mine'] as const,
  pendingFirstAscensionistLinks: () => ['first-ascensionist-links', 'pending'] as const,
  myOpenings: () => ['routes', 'my-openings'] as const,
  firstAscensionistBySlug: (slug: string) => ['first-ascensionists', 'by-slug', slug] as const,
  routesByFirstAscensionist: (id: number) => ['routes', 'by-first-ascensionist', id] as const,
  pendingRoutes: () => ['routes', 'pending'] as const,
  pendingAreas: () => ['areas', 'pending'] as const,
  pendingFirstAscensionists: () => ['first-ascensionists', 'pending'] as const,
  areaBySlugPath: (path: string) => ['areas', 'by-slug-path', path] as const,
  routesByArea: (areaId: number) => ['routes', 'by-area', areaId] as const,
  areaList: (refreshToken?: number) => ['areas', 'list', refreshToken ?? null] as const,
  firstAscensionistList: () => ['first-ascensionists', 'list'] as const,
  firstAscensionistLinkCandidates: (q: string) => ['first-ascensionists', 'link-candidates', q] as const,
  areaChildren: (areaId: number) => ['areas', 'children', areaId] as const,
  manageRootAreas: (page: number) => ['areas', 'roots', 'manage', page] as const,
  passkeys: () => ['passkeys'] as const,
  areaPolygons: () => ['areas', 'polygons'] as const,
};
