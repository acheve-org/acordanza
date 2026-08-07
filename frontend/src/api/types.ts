export interface GeoPoint {
  lat: number;
  lng: number;
}

export interface AreaSummary {
  id: number;
  name: string;
  slug: string;
  parentAreaId: number | null;
  polygon: GeoPoint[];
  centroidLat: number | null;
  centroidLng: number | null;
  validatedAt: string | null;
  validatedBy: string | null;
}

export interface AreaBounds {
  minLat: number;
  minLng: number;
  maxLat: number;
  maxLng: number;
}

export interface AreaNode {
  id: number;
  name: string;
  slug: string;
  hasChildren: boolean;
}

export interface PagedAreaNodes {
  items: AreaNode[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AreaAncestor {
  id: number;
  name: string;
  slug: string;
}

export interface AreaChild {
  id: number;
  name: string;
  slug: string;
  hasChildren: boolean;
}

export interface AreaDetail {
  id: number;
  name: string;
  slug: string;
  parentAreaId: number | null;
  ancestors: AreaAncestor[];
  children: AreaChild[];
  validatedAt: string | null;
  validatedBy: string | null;
  canManageMedia: boolean;
  media: TopoSummary[];
}

export interface AreaExpert {
  id: number;
  areaId: number;
  areaName: string;
  userId: string;
  userName: string;
  requestedAt: string;
  status: 'pending' | 'approved' | 'rejected';
  decidedAt: string | null;
  decidedByName: string | null;
  rejectionReason: string | null;
}

export interface FirstAscensionistSummary {
  id: number;
  name: string;
  userId: string | null;
  linkedUserName: string | null;
}

export interface FirstAscensionistLinkRequestSummary {
  id: number;
  firstAscensionistId: number;
  firstAscensionistName: string;
  userId: string;
  userName: string;
  requestedAt: string;
  status: 'pending' | 'approved' | 'rejected';
  decidedAt: string | null;
  decidedByName: string | null;
  rejectionReason: string | null;
}

export interface TopoSummary {
  id: number;
  url: string;
  fileName: string;
  createdAt: string;
  uploadedBy: string;
}

export interface RouteSummary {
  id: number;
  name: string;
  areaId: number;
  areaName: string;
  areaPath: string;
  grade: string;
  orientation: string | null;
  gear: string | null;
  description: string;
  tags: string;
  firstAscensionists: FirstAscensionistSummary[];
  addedBy: string;
  createdAt: string;
  hasTick: boolean;
  hasTopos: boolean;
  validatedAt: string | null;
  validatedBy: string | null;
  canValidate: boolean;
}

export interface RouteDetail extends Omit<RouteSummary, 'hasTopos'> {
  topos: TopoSummary[];
}

export interface RouteFilters {
  q: string;
  areaId: string;
  gear: string;
  tag: string;
  hasTopo: string;
}

export interface TickFilters {
  q: string;
  areaId: string;
  gear: string;
  tag: string;
}

export interface TickedRoute {
  tickId: number;
  routeId: number;
  routeName: string;
  areaId: number;
  areaName: string;
  areaPath: string;
  grade: string;
  orientation: string | null;
  gear: string | null;
  tags: string;
  date: string;
  rating: number | null;
  notes: string;
  createdAt: string;
}

export interface PagedTicks {
  items: TickedRoute[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UpsertRouteInput {
  name: string;
  areaId: number;
  grade: string;
  orientation: string;
  gear: string;
  description: string;
  tags: string;
  firstAscensionistIds: number[];
}

export interface CreateTickInput {
  date: string;
  rating: number | null;
  notes: string;
}

export interface LoginResponse {
  token: string | null;
  username: string | null;
  mfaRequired: boolean;
  sessionId: string | null;
  options: unknown;
}

export interface MeResponse {
  id: string;
  username: string;
  name: string;
  role: string;
  permissions: string[];
  isFirstAscensionist: boolean;
  isAreaExpert: boolean;
}

export interface PasskeySummary {
  id: number;
  name: string;
  createdAt: string;
}

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

export interface UserAccount {
  id: string;
  name: string;
  email: string;
  role: string;
  pending: boolean;
}

export interface InviteUserInput {
  email: string;
  role: string;
}

export interface PagedUsers {
  items: UserAccount[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UserFilters {
  q: string;
  role: string;
  page: number;
  sortBy: 'name' | 'email' | 'role';
  sortDir: 'asc' | 'desc';
}

export interface MyInvite {
  email: string;
  pending: boolean;
  hasInvitedOthers: boolean;
}

export interface MyInvitesResponse {
  invites: MyInvite[];
  invitesLeft: number | null;
  maxInvites: number | null;
}

// Mirrors backend/Acordanza.Api/Authorization/Roles.cs
export const roleLabels: Record<string, string> = {
  admin: 'Administrador',
  moderator: 'Moderador',
  user: 'Usuario',
};
