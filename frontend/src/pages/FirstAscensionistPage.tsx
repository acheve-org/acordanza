import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { Button, Chip, EmptyState } from 'acordanza-design-system';
import { RoutesList } from '../components/RoutesList';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as routesApi from '../api/routes';
import type { FirstAscensionistProfile, RouteSummary } from '../api/types';

export function FirstAscensionistPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const slug = useParams().slug!;

  const [firstAscensionist, setFirstAscensionist] = useState<FirstAscensionistProfile | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  // Navigating straight from one aperturista to another keeps this component mounted, so a
  // slow response for the one we just left can land after the new one's. Every setState off a
  // fetch is gated on this still naming the aperturista on screen — same stale-response guard
  // `AreaMap` uses for its bounds fetches.
  const shownSlug = useRef(slug);

  function refreshRoutes(forId: number) {
    routesApi.listRoutesByFirstAscensionist(forId).then((result) => {
      if (shownSlug.current === slug) setRoutes(result);
    });
  }

  useEffect(() => {
    shownSlug.current = slug;
    setFirstAscensionist(null);
    setNotFound(false);
    setRoutes([]);
    firstAscensionistsApi
      .getFirstAscensionistBySlug(slug)
      .then((fa) => {
        if (shownSlug.current !== slug) return;
        setFirstAscensionist(fa);
        refreshRoutes(fa.id);
      })
      .catch(() => {
        if (shownSlug.current === slug) setNotFound(true);
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [slug]);

  function handleSelectRoute(route: RouteSummary) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

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
