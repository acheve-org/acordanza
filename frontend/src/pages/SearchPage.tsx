import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Filters } from '../components/Filters';
import { RoutesList } from '../components/RoutesList';
import * as routesApi from '../api/routes';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import type { BackgroundLocationState } from '../api/backgroundLocation';
import type { RouteFilters, RouteSummary } from '../api/types';
import { emptyRouteFilters } from '../api/types';

export function SearchPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const splat = useParams()['*'];
  const [filters, setFilters] = useState<RouteFilters>(emptyRouteFilters);
  const debouncedFilters = useDebouncedValue(filters);

  // Fresh-visit fallback (plan 21): a pasted /routes/{area-slug-path}/{route-slug}
  // link, or a reload on one, has no backgroundLocation in router state — this route
  // (App.tsx's "routes/*" catch-all) still lands on SearchPage, so synthesize the
  // state once so RouteDetailOverlay is the only component that ever opens the modal,
  // regardless of how the URL was reached.
  useEffect(() => {
    if (splat && !(location.state as BackgroundLocationState | null)?.backgroundLocation) {
      navigate(`/routes/${splat}`, { replace: true, state: { backgroundLocation: { pathname: '/routes' } } });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [splat]);

  const { data: routes = [], error: routesError } = useQuery({
    queryKey: queryKeys.routes(debouncedFilters),
    queryFn: () => routesApi.listRoutes(debouncedFilters),
  });

  function handleSelect(route: RouteSummary) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

  return (
    <main className="layout">
      <Filters filters={filters} onChange={setFilters} onAddRoute={() => navigate('/routes/new')} />
      {routesError ? (
        <p className="error-text">{errorMessage(routesError, 'No se pudieron cargar las vías')}</p>
      ) : (
        <RoutesList routes={routes} onSelect={handleSelect} />
      )}
    </main>
  );
}
