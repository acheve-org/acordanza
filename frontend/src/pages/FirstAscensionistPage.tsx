import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Button, Chip, EmptyState } from 'acordanza-design-system';
import { RoutesList } from '../components/RoutesList';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as routesApi from '../api/routes';
import { queryKeys } from '../api/queryKeys';
import { ApiError } from '../api/client';
import type { RouteSummary } from '../api/types';

export function FirstAscensionistPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const slug = useParams().slug!;

  const { data: firstAscensionist, error } = useQuery({
    queryKey: queryKeys.firstAscensionistBySlug(slug),
    queryFn: () => firstAscensionistsApi.getFirstAscensionistBySlug(slug),
  });

  const { data: routes = [] } = useQuery({
    queryKey: queryKeys.routesByFirstAscensionist(firstAscensionist?.id ?? 0),
    queryFn: () => routesApi.listRoutesByFirstAscensionist(firstAscensionist!.id),
    enabled: !!firstAscensionist,
  });

  function handleSelectRoute(route: RouteSummary) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

  const notFound = error instanceof ApiError && error.status === 404;

  if (notFound) {
    return (
      <main className="fa-page">
        <EmptyState>Este aperturista no existe.</EmptyState>
        <Button as={Link} variant="ghost" small to="/fa">
          Volver a aperturistas
        </Button>
      </main>
    );
  }

  if (!firstAscensionist) return null;

  return (
    <main className="fa-page">
      <nav className="area-breadcrumb" aria-label="Ruta de navegación">
        <Link to="/fa">Aperturistas</Link>
        <span className="area-breadcrumb-sep">›</span>
        <span className="area-breadcrumb-current">{firstAscensionist.name}</span>
      </nav>

      <h1>{firstAscensionist.name}</h1>
      <p className="detail-fa">
        {firstAscensionist.routeCount === 1 ? '1 vía abierta' : `${firstAscensionist.routeCount} vías abiertas`}
        {firstAscensionist.linkedUserName && ` · Cuenta de ${firstAscensionist.linkedUserName}`}
      </p>
      {firstAscensionist.areaNames.length > 0 && (
        <div className="tag-row">
          {firstAscensionist.areaNames.map((areaName) => (
            <Chip key={areaName}>{areaName}</Chip>
          ))}
        </div>
      )}

      <RoutesList routes={routes} onSelect={handleSelectRoute} />
    </main>
  );
}
