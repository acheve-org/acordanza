import type { components } from './generated';
import { orientationLabels as designSystemOrientationLabels, orientations as designSystemOrientations } from 'acordanza-design-system';
import type { Orientation } from 'acordanza-design-system';

export type { Orientation };

export type GeoPoint = components['schemas']['GeoPointDto'];

// Backend/Acordanza.Api/Models/ReviewStatus.cs is a free-text string column (both the
// request-review flows' Status and the validation lifecycle's ValidationStatus), but its
// runtime values are the closed set below — openapi-typescript can't know that from the
// schema alone, so narrow it by hand.
type RequestStatus = 'pending' | 'approved' | 'rejected';

export type AreaSummary = Omit<components['schemas']['AreaDto'], 'validationStatus'> & {
  validationStatus: RequestStatus;
};

// Not a backend DTO — a client-constructed viewport query, not a response shape.
export interface AreaBounds {
  minLat: number;
  minLng: number;
  maxLat: number;
  maxLng: number;
}

export type AreaNode = components['schemas']['AreaNodeDto'];

export type PagedAreaNodes = components['schemas']['PagedAreaNodesResponse'];

export type AreaAncestor = components['schemas']['AreaAncestorDto'];

export type AreaChild = components['schemas']['AreaChildDto'];

export type AreaDetail = Omit<components['schemas']['AreaDetailDto'], 'validationStatus'> & {
  validationStatus: RequestStatus;
};

// The review queue's view of a not-yet-decided area — GET /areas/pending.
export type AreaPending = components['schemas']['AreaPendingDto'];

export type AreaExpert = Omit<components['schemas']['AreaExpertDto'], 'status'> & { status: RequestStatus };

export type FirstAscensionistSummary = components['schemas']['FirstAscensionistDto'];

// The "Sigue" section's view of a first ascensionist: the plain DTO plus what they opened.
export type FirstAscensionistProfile = Omit<components['schemas']['FirstAscensionistProfileDto'], 'validationStatus'> & {
  validationStatus: RequestStatus;
};

// The review queue's view of a not-yet-decided first ascensionist — GET /first-ascensionists/pending.
export type FirstAscensionistPending = components['schemas']['FirstAscensionistPendingDto'];

export type FirstAscensionistLinkRequestSummary =
  Omit<components['schemas']['FirstAscensionistLinkRequestDto'], 'status'> & { status: RequestStatus };

export type TopoSummary = components['schemas']['TopoDto'];

// RouteImport.Status and .Kind are string columns on the backend for the same reason
// ValidationStatus is (see RequestStatus above), but their runtime values are these closed
// sets — narrowed by hand, since openapi-typescript can't see them from the schema.
export type RouteImportStatus = 'pending' | 'completed' | 'discarded' | 'failed';

// What the user handed us to read: a topo photo or the URL of a page. FileName is set for
// the first, SourceUrl for the second — never both.
export type RouteImportKind = 'image' | 'url';

export type RouteImport = Omit<components['schemas']['RouteImportDto'], 'status' | 'kind'> & {
  status: RouteImportStatus;
  kind: RouteImportKind;
};

// A third label map alongside reviewStatusLabels/validationStatusLabels, and separate for
// the same reason those two are: the four states here are the import pipeline's, not a
// review's — "Descartado" is the model's own answer, "Falló" is the pipeline breaking, and
// only the second is worth retrying. "Leyendo" covers both kinds, since a row's title
// already says which one it is.
export const routeImportStatusLabels: Record<RouteImportStatus, string> = {
  pending: 'Leyendo',
  completed: 'Completado',
  discarded: 'Descartado',
  failed: 'Falló',
};

export const routeImportKindLabels: Record<RouteImportKind, string> = {
  image: 'Foto',
  url: 'Enlace',
};

export type RouteSummary = Omit<components['schemas']['RouteDto'], 'validationStatus'> & {
  validationStatus: RequestStatus;
};

export type RouteDetail = Omit<components['schemas']['RouteDetailDto'], 'validationStatus'> & {
  validationStatus: RequestStatus;
};

// The review queue's view of a not-yet-decided route — GET /routes/pending.
export type RoutePending = components['schemas']['RoutePendingDto'];

// "Mis contribuciones" (issue #125) — the areas/routes/first-ascensionists the current
// user created, each with its validation status.
export type Contribution = Omit<components['schemas']['ContributionDto'], 'validationStatus'> & {
  validationStatus: RequestStatus;
};

// One page of one kind — GET /contributions/{routes|areas|first-ascensionists}. `items` is
// re-added narrowed for the same reason `Contribution` narrows: the contract types
// validationStatus as a plain string.
export type PagedContributions = Omit<components['schemas']['PagedContributionsResponse'], 'items'> & {
  items: Contribution[];
};

export interface RouteFilters {
  q: string;
  areaId: string;
  grade: string;
  aidGrade: string;
  orientation: Orientation[];
  gear: string;
  hasTopo: string;
}

export const emptyRouteFilters: RouteFilters = {
  q: '',
  areaId: '',
  grade: '',
  aidGrade: '',
  orientation: [],
  gear: '',
  hasTopo: '',
};

export interface TickFilters {
  q: string;
  areaId: string;
  gear: string;
}

export type TickedRoute = components['schemas']['MyTickDto'];

export type PagedTicks = components['schemas']['PagedMyTicksResponse'];

export type UpsertRouteInput = components['schemas']['UpsertRouteRequest'];

export type CreateTickInput = components['schemas']['CreateTickRequest'];

export type LoginResponse = components['schemas']['LoginResponse'];

export type MeResponse = components['schemas']['MeResponse'];

export type PasskeySummary = components['schemas']['PasskeySummary'];

// Route `orientation` is ordered, angular data — the ordered list and Spanish labels
// live in the design system's Compass module (its geometry needs the ordering too) and
// are re-exported here so every existing call site keeps working unchanged.
export const orientations = designSystemOrientations;
export const orientationLabels = designSystemOrientationLabels;

// Route `gear` values are stored in English to match the backend; this maps them to
// the Spanish labels shown in the UI (see root CLAUDE.md — all user-facing text is Spanish).

export const gearLabels: Record<string, string> = {
  bolted: 'Equipada',
  removable: 'Flotantes',
  pitons: 'Clavos',
  aiders: 'Estribos',
};

export type UserAccount = components['schemas']['UserAccountDto'];

export type InviteUserInput = components['schemas']['InviteUserRequest'];

export type PagedUsers = components['schemas']['PagedUsersResponse'];

export interface UserFilters {
  q: string;
  role: string;
  page: number;
  sortBy: 'name' | 'email' | 'role';
  sortDir: 'asc' | 'desc';
}

export type MyInvite = components['schemas']['MyInviteDto'];

export type MyInvitesResponse = components['schemas']['MyInvitesResponse'];

// Mirrors backend/Acordanza.Api/Authorization/Roles.cs
export const roleLabels: Record<string, string> = {
  admin: 'Administrador',
  moderator: 'Moderador',
  user: 'Usuario',
};

// Shared by MyAreasPage/ClaimFirstAscensionistPage's self-service request flows
// (AreaExpert, FirstAscensionistLinkRequest) — someone waiting on an answer.
export const reviewStatusLabels: Record<RequestStatus, string> = {
  pending: 'Pendiente de aprobación',
  approved: 'Aprobado',
  rejected: 'Rechazado',
};

// Shared by every IValidatable's editorial lifecycle (routes, areas, first
// ascensionists) — MyContributionsPage, DetailModal, AreaPage, PendingTasksPage.
export const validationStatusLabels: Record<RequestStatus, string> = {
  pending: 'Pendiente de validación',
  approved: 'Validado',
  rejected: 'Rechazado',
};
