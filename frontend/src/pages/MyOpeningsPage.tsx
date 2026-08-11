import { Navigate, useLocation, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { RoutesList } from '../components/RoutesList';
import * as routesApi from '../api/routes';
import { queryKeys } from '../api/queryKeys';
import type { RouteSummary } from '../api/types';
import { useAuth } from '../context/AuthContext';

export function MyOpeningsPage() {
  const { isFirstAscensionist } = useAuth();

  const navigate = useNavigate();
  const location = useLocation();

  const { data: routes = [] } = useQuery({
    queryKey: queryKeys.myOpenings(),
    queryFn: () => routesApi.listMyOpenings(),
    enabled: isFirstAscensionist,
  });

  if (!isFirstAscensionist) return <Navigate to="/my-acordanza" replace />;

  function handleSelectRoute(route: RouteSummary) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

  return (
    <main className="layout">
      <RoutesList routes={routes} onSelect={handleSelectRoute} />
    </main>
  );
}
