export interface CragSummary {
  id: number;
  name: string;
  parentCragId: number | null;
}

export interface RouteOpenerSummary {
  id: number;
  name: string;
  userId: string | null;
  linkedUserName: string | null;
}

export interface UserSummary {
  id: string;
  name: string;
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
  cragId: number;
  cragName: string;
  cragPath: string;
  grade: string;
  orientation: string | null;
  gear: string | null;
  description: string;
  tags: string;
  openers: RouteOpenerSummary[];
  addedBy: string;
  createdAt: string;
  tickCount: number;
  avgRating: number | null;
  hasTopos: boolean;
  validatedAt: string | null;
  validatedBy: string | null;
}

export interface Tick {
  id: number;
  routeId: number;
  climber: string;
  date: string;
  rating: number | null;
  notes: string;
  createdAt: string;
}

export interface RouteDetail extends Omit<RouteSummary, 'tickCount' | 'avgRating' | 'hasTopos'> {
  ticks: Tick[];
  topos: TopoSummary[];
}

export interface RouteFilters {
  q: string;
  cragId: string;
  gear: string;
  tag: string;
  hasTopo: string;
}

export interface UpsertRouteInput {
  name: string;
  cragId: number;
  grade: string;
  orientation: string;
  gear: string;
  description: string;
  tags: string;
  openerIds: number[];
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
  username: string;
  name: string;
  role: string;
  permissions: string[];
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
