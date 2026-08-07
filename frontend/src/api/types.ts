import type { components } from './generated';

export type GeoPoint = components['schemas']['GeoPointDto'];

export type AreaSummary = components['schemas']['AreaDto'];

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

export type AreaDetail = components['schemas']['AreaDetailDto'];

// Backend/Acordanza.Api/Models/AreaExpert.cs's Status is a free-text string column, but its
// runtime values are the closed set below (see AreaExpertsController) — openapi-typescript
// can't know that from the schema alone, so narrow it by hand.
type RequestStatus = 'pending' | 'approved' | 'rejected';

export type AreaExpert = Omit<components['schemas']['AreaExpertDto'], 'status'> & { status: RequestStatus };

export type FirstAscensionistSummary = components['schemas']['FirstAscensionistDto'];

// The "Sigue" section's view of a first ascensionist: the plain DTO plus what they opened.
export type FirstAscensionistProfile = components['schemas']['FirstAscensionistProfileDto'];

export type FirstAscensionistLinkRequestSummary =
  Omit<components['schemas']['FirstAscensionistLinkRequestDto'], 'status'> & { status: RequestStatus };

export type TopoSummary = components['schemas']['TopoDto'];

export type RouteSummary = components['schemas']['RouteDto'];

export type RouteDetail = components['schemas']['RouteDetailDto'];

export interface RouteFilters {
  q: string;
  areaId: string;
  grade: string;
  orientation: string;
  gear: string;
  tag: string;
  hasTopo: string;
}

export const emptyRouteFilters: RouteFilters = {
  q: '',
  areaId: '',
  grade: '',
  orientation: '',
  gear: '',
  tag: '',
  hasTopo: '',
};

export interface TickFilters {
  q: string;
  areaId: string;
  gear: string;
  tag: string;
}

export type TickedRoute = components['schemas']['MyTickDto'];

export type PagedTicks = components['schemas']['PagedMyTicksResponse'];

export type UpsertRouteInput = components['schemas']['UpsertRouteRequest'];

export type CreateTickInput = components['schemas']['CreateTickRequest'];

export type LoginResponse = components['schemas']['LoginResponse'];

export type MeResponse = components['schemas']['MeResponse'];

export type PasskeySummary = components['schemas']['PasskeySummary'];

// Route `orientation`/`gear` values are stored in English to match the backend; these map them to
// the Spanish labels shown in the UI (see root CLAUDE.md — all user-facing text is Spanish).
export const orientationLabels: Record<string, string> = {
  north: 'Norte',
  northeast: 'Noreste',
  east: 'Este',
  southeast: 'Sureste',
  south: 'Sur',
  southwest: 'Suroeste',
  west: 'Oeste',
  northwest: 'Noroeste',
};

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
