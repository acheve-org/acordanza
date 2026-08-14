import type { components } from '../api/generated';
import type {
  AreaDetail,
  AreaSummary,
  Contribution,
  FirstAscensionistProfile,
  RouteDetail,
  RouteSummary,
  TickedRoute,
  TopoImport,
} from '../api/types';

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
type FirstAscensionistProfileDto = FirstAscensionistProfile;
type RoutePendingDto = components['schemas']['RoutePendingDto'];
type AreaPendingDto = components['schemas']['AreaPendingDto'];
type AreaExpertDto = components['schemas']['AreaExpertDto'];
type FirstAscensionistLinkRequestDto = components['schemas']['FirstAscensionistLinkRequestDto'];
type TopoDto = components['schemas']['TopoDto'];
type MeResponse = components['schemas']['MeResponse'];
type LoginResponse = components['schemas']['LoginResponse'];
type UserAccountDto = components['schemas']['UserAccountDto'];
// Like makeRoute/makeArea, off the narrowed app type so a result is directly assignable
// where validationStatus is the closed RequestStatus set.
type ContributionDto = Contribution;
type AreaDetailDto = AreaDetail;
// Same narrowing again: TopoImportDto.status is a plain string in the contract but a
// closed four-value set at runtime (see api/types.ts's TopoImportStatus).
type TopoImportDto = TopoImport;

export const makeTickedRoute = (o: Partial<TickedRoute> = {}): TickedRoute => ({
  tickId: 1,
  routeId: 1,
  routeName: 'Travesía del Gato',
  routeSlug: 'travesia-del-gato',
  areaId: 1,
  areaName: 'Peña Rúa',
  areaPath: 'Peña Rúa',
  areaSlugPath: 'pena-rua',
  grade: '6a',
  orientation: 'south',
  gear: 'bolted',
  length: 100,
  date: '2026-01-01',
  rating: null,
  notes: '',
  createdAt: '2026-01-01T00:00:00Z',
  ...o,
});

export const makeFirstAscensionist = (o: Partial<FirstAscensionistDto> = {}): FirstAscensionistDto => ({
  id: 1,
  name: 'Alex Aperturista',
  userId: null,
  linkedUserName: null,
  ...o,
});

export const makeFirstAscensionistProfile = (
  o: Partial<FirstAscensionistProfileDto> = {},
): FirstAscensionistProfileDto => ({
  id: 1,
  name: 'Alex Aperturista',
  slug: 'alex-aperturista',
  userId: null,
  linkedUserName: null,
  routeCount: 0,
  areaNames: [],
  validationStatus: 'approved',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
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
  slug: 'travesia-del-gato',
  areaId: 1,
  areaName: 'Peña Rúa',
  areaPath: 'Peña Rúa',
  areaSlugPath: 'pena-rua',
  grade: '6a',
  orientation: 'south',
  gear: 'bolted',
  description: 'Una vía clásica.',
  length: 100,
  obligatoryGrade: null,
  recommendedGear: null,
  optionalGear: null,
  links: [],
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
  slug: 'travesia-del-gato',
  areaId: 1,
  areaName: 'Peña Rúa',
  areaPath: 'Peña Rúa',
  areaSlugPath: 'pena-rua',
  grade: '6a',
  orientation: 'south',
  gear: 'bolted',
  description: 'Una vía clásica.',
  length: 100,
  obligatoryGrade: null,
  recommendedGear: null,
  optionalGear: null,
  links: [],
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
  enrichedAt: null,
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

export const makeAreaDetail = (o: Partial<AreaDetailDto> = {}): AreaDetailDto => ({
  id: 1,
  name: 'Peña Rúa',
  slug: 'pena-rua',
  parentAreaId: null,
  ancestors: [],
  children: [],
  validationStatus: 'approved',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  canManageMedia: false,
  media: [],
  ...o,
});

export const makeRoutePending = (o: Partial<RoutePendingDto> = {}): RoutePendingDto => ({
  id: 1,
  name: 'Travesía del Gato',
  slug: 'travesia-del-gato',
  areaPath: 'Peña Rúa',
  areaSlugPath: 'pena-rua',
  grade: '6a',
  createdAt: '2026-01-01T00:00:00Z',
  addedByName: 'Alex',
  ...o,
});

export const makeAreaPending = (o: Partial<AreaPendingDto> = {}): AreaPendingDto => ({
  id: 1,
  name: 'Peña Rúa',
  namePath: 'Peña Rúa',
  parentAreaId: null,
  createdAt: '2026-01-01T00:00:00Z',
  addedByName: 'Alex',
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

export const makeContribution = (o: Partial<ContributionDto> = {}): ContributionDto => ({
  id: 1,
  name: 'Vía de prueba',
  detail: 'Peña Rúa › Sector 1',
  createdAt: '2026-01-01T00:00:00Z',
  validationStatus: 'pending',
  decidedAt: null,
  decidedByName: null,
  rejectionReason: null,
  ...o,
});

export const makeTopoImport = (o: Partial<TopoImportDto> = {}): TopoImportDto => ({
  id: 1,
  fileName: 'topo.jpg',
  areaId: 1,
  areaNamePath: 'Peña Rúa › Sector 1',
  createdAt: '2026-01-01T00:00:00Z',
  status: 'pending',
  outcome: null,
  routeId: null,
  routeName: null,
  routeSlug: null,
  routeAreaSlugPath: null,
  routeWasCreated: false,
  processedAt: null,
  ...o,
});

export const makeUserAccount = (o: Partial<UserAccountDto> = {}): UserAccountDto => ({
  id: 'user-1',
  name: 'Alex',
  email: 'alex@example.com',
  role: 'user',
  pending: false,
  invitedByName: null,
  invitedAt: null,
  confirmedAt: null,
  ...o,
});
