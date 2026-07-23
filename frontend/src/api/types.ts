export interface RouteSummary {
  id: number;
  name: string;
  crag: string;
  grade: string;
  gradeSystem: string;
  type: string;
  description: string;
  tags: string;
  firstAscent: string;
  addedBy: string;
  createdAt: string;
  tickCount: number;
  avgRating: number | null;
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

export interface RouteDetail extends Omit<RouteSummary, 'tickCount' | 'avgRating'> {
  ticks: Tick[];
}

export interface RouteFilters {
  q: string;
  crag: string;
  type: string;
  tag: string;
}

export interface UpsertRouteInput {
  name: string;
  crag: string;
  grade: string;
  gradeSystem: string;
  type: string;
  description: string;
  tags: string;
  firstAscent: string;
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

export interface PasskeySummary {
  id: number;
  name: string;
  createdAt: string;
}

// Route `type` values are stored in English to match the backend; this maps them to
// the Spanish labels shown in the UI (see root CLAUDE.md — all user-facing text is Spanish).
export const routeTypeLabels: Record<string, string> = {
  sport: 'Deportiva',
  trad: 'Clásica',
  boulder: 'Bloque',
  toprope: 'Top rope',
  alpine: 'Alpina',
};
