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
