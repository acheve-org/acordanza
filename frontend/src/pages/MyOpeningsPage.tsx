import { useEffect, useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { RoutesList } from '../components/RoutesList';
import * as routesApi from '../api/routes';
import type { RouteSummary } from '../api/types';
import { useAuth } from '../context/AuthContext';

export function MyOpeningsPage() {
  const { isFirstAscensionist } = useAuth();
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    routesApi.listMyOpenings().then(setRoutes);
  }, []);

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
