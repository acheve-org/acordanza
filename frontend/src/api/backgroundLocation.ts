import type { Location } from 'react-router';

// Shared shape for the react-router "background location" pattern (App.tsx,
// RouteDetailOverlay, and the six pages that open a route's detail overlay): a route
// detail URL carries the location the user opened it from in router state, so
// App.tsx's outer <Routes> keeps rendering that page underneath, and closing the
// overlay navigates back to exactly it.
export interface BackgroundLocationState {
  backgroundLocation?: Location;
}
