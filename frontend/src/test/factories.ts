import type { components } from '../api/generated';
import type { AreaSummary, RouteDetail, RouteSummary } from '../api/types';

// Typed off the generated OpenAPI contract (see api/generated.ts) rather than hand-rolled:
// a backend DTO change that regenerates generated.ts breaks these at `tsc -b` build time,
// not just the app, so mocks can't silently drift from the real API shape.
// One factory per DTO the test suite constructs, each overridable via `Partial<...>`.
//
// RouteDto/RouteDetailDto/AreaDto's validationStatus is narrowed by api/types.ts (a plain
// string in the generated contract, but a closed 'pending'|'approved'|'rejected' set at
// runtime) — makeRoute/makeRouteDetail/makeArea build off those narrowed app types instead
// of the raw generated ones so a factory result can be passed directly as a typed prop
// (e.g. DetailModal's `route`), not just used inside an untyped HttpResponse.json(...) mock.

type RouteDto = RouteSummary;
type RouteDetailDto = RouteDetail;
type AreaDto = AreaSummary;
type AreaNodeDto = components['schemas']['AreaNodeDto'];
type PagedAreaNodesResponse = components['schemas']['PagedAreaNodesResponse'];
type FirstAscensionistDto = components['schemas']['FirstAscensionistDto'];
type AreaExpertDto = components['schemas']['AreaExpertDto'];
type FirstAscensionistLinkRequestDto = components['schemas']['FirstAscensionistLinkRequestDto'];
type TopoDto = components['schemas']['TopoDto'];
type MeResponse = components['schemas']['MeResponse'];
type LoginResponse = components['schemas']['LoginResponse'];
type UserAccountDto = components['schemas']['UserAccountDto'];

export const makeFirstAscensionist = (o: Partial<FirstAscensionistDto> = {}): FirstAscensionistDto => ({
  id: 1,
  name: 'Alex Aperturista',
  userId: null,
  linkedUserName: null,
  ...o,
});

export const makeTopo = (o: Partial<TopoDto> = {}): TopoDto => ({
  id: 1,
  url: 'https://example.com/topo.jpg',
  fileName: 'topo.jpg',
  createdAt: '2026-01-01T00:00:00Z',
  uploadedBy: 'Alex',
  ...o,
});

export const makeRoute = (o: Partial<RouteDto> = {}): RouteDto => ({
  id: 1,
  name: 'Travesía del Gato',
  areaId: 1,
  areaName: 'Peña Rúa',
  areaPath: 'Peña Rúa',
  grade: '6a',
  orientation: 'south',
  gear: 'bolted',
  description: 'Una vía clásica.',
  tags: 'clásica,regletas',
  firstAscensionists: [makeFirstAscensionist()],
  addedBy: 'Alex',
  createdAt: '2026-01-01T00:00:00Z',
  hasTick: false,
  hasTopos: false,
  validationStatus: 'pending',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  canValidate: false,
  ...o,
});

export const makeRouteDetail = (o: Partial<RouteDetailDto> = {}): RouteDetailDto => ({
  id: 1,
  name: 'Travesía del Gato',
  areaId: 1,
  areaName: 'Peña Rúa',
  areaPath: 'Peña Rúa',
  grade: '6a',
  orientation: 'south',
  gear: 'bolted',
  description: 'Una vía clásica.',
  tags: 'clásica,regletas',
  firstAscensionists: [makeFirstAscensionist()],
  addedBy: 'Alex',
  createdAt: '2026-01-01T00:00:00Z',
  hasTick: false,
  topos: [],
  validationStatus: 'pending',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  canValidate: false,
  ...o,
});

export const makeArea = (o: Partial<AreaDto> = {}): AreaDto => ({
  id: 1,
  name: 'Peña Rúa',
  slug: 'pena-rua',
  parentAreaId: null,
  polygon: [],
  centroidLat: null,
  centroidLng: null,
  validationStatus: 'pending',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  ...o,
});

export const makeAreaNode = (o: Partial<AreaNodeDto> = {}): AreaNodeDto => ({
  id: 1,
  name: 'Peña Rúa',
  slug: 'pena-rua',
  hasChildren: false,
  hasPolygon: false,
  ...o,
});

export const makePagedAreaNodes = (o: Partial<PagedAreaNodesResponse> = {}): PagedAreaNodesResponse => ({
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 10,
  ...o,
});

export const makeAreaExpert = (o: Partial<AreaExpertDto> = {}): AreaExpertDto => ({
  id: 1,
  areaId: 1,
  areaName: 'Peña Rúa',
  userId: 'user-1',
  userName: 'Alex',
  requestedAt: '2026-01-01T00:00:00Z',
  status: 'pending',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  ...o,
});

export const makeFirstAscensionistLinkRequest = (
  o: Partial<FirstAscensionistLinkRequestDto> = {},
): FirstAscensionistLinkRequestDto => ({
  id: 1,
  firstAscensionistId: 1,
  firstAscensionistName: 'Alex Aperturista',
  userId: 'user-1',
  userName: 'Alex',
  requestedAt: '2026-01-01T00:00:00Z',
  status: 'pending',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  ...o,
});

export const makeMeResponse = (o: Partial<MeResponse> = {}): MeResponse => ({
  id: 'user-1',
  username: 'alex@example.com',
  name: 'Alex',
  role: 'user',
  permissions: [],
  isFirstAscensionist: false,
  isAreaExpert: false,
  ...o,
});

export const makeLoginResponse = (o: Partial<LoginResponse> = {}): LoginResponse => ({
  token: 'test-token',
  username: 'alex@example.com',
  mfaRequired: false,
  sessionId: null,
  options: null,
  ...o,
});

export const makeUserAccount = (o: Partial<UserAccountDto> = {}): UserAccountDto => ({
  id: 'user-1',
  name: 'Alex',
  email: 'alex@example.com',
  role: 'user',
  pending: false,
  ...o,
});
