import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Button, Chip, EmptyState } from 'acordanza-design-system';
import { RoutesList } from '../components/RoutesList';
import { DetailModal } from '../components/DetailModal';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as routesApi from '../api/routes';
import * as toposApi from '../api/topos';
import type { CreateTickInput, FirstAscensionistProfile, RouteDetail, RouteSummary } from '../api/types';

export function FirstAscensionistPage() {
  const navigate = useNavigate();
  const id = Number(useParams().id);

  const [firstAscensionist, setFirstAscensionist] = useState<FirstAscensionistProfile | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [detailRoute, setDetailRoute] = useState<RouteDetail | null>(null);

  // Navigating straight from one aperturista to another keeps this component mounted, so a
  // slow response for the one we just left can land after the new one's. Every setState off a
  // fetch is gated on this still naming the aperturista on screen — same stale-response guard
  // `AreaMap` uses for its bounds fetches.
  const shownId = useRef(id);

  function refreshRoutes(forId: number) {
    routesApi.listRoutesByFirstAscensionist(forId).then((result) => {
      if (shownId.current === forId) setRoutes(result);
    });
  }

  useEffect(() => {
    shownId.current = id;
    setFirstAscensionist(null);
    setNotFound(false);
    setRoutes([]);
    firstAscensionistsApi
      .getFirstAscensionist(id)
      .then((fa) => {
        if (shownId.current === id) setFirstAscensionist(fa);
      })
      .catch(() => {
        if (shownId.current === id) setNotFound(true);
      });
    refreshRoutes(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  useEffect(() => {
    if (detailRouteId === null) {
      setDetailRoute(null);
      return;
    }
    routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }, [detailRouteId]);

  function refreshDetail() {
    if (detailRouteId !== null) routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }

  async function handleDeleteRoute() {
    if (!detailRouteId) return;
    await routesApi.deleteRoute(detailRouteId);
    setDetailRouteId(null);
    refreshRoutes(id);
  }

  async function handleAddTick(input: CreateTickInput) {
    if (!detailRouteId) return;
    await routesApi.createTick(detailRouteId, input);
    refreshDetail();
    refreshRoutes(id);
  }

  async function handleUploadTopo(file: File) {
    if (!detailRouteId) return;
    await toposApi.uploadTopo(detailRouteId, file);
    refreshDetail();
    refreshRoutes(id);
  }

  async function handleDeleteTopo(topoId: number) {
    await toposApi.deleteTopo(topoId);
    refreshDetail();
    refreshRoutes(id);
  }

  async function handleValidateRoute() {
    if (!detailRouteId) return;
    await routesApi.validateRoute(detailRouteId);
    refreshDetail();
    refreshRoutes(id);
  }

  async function handleRejectRoute(reason: string) {
    if (!detailRouteId) return;
    await routesApi.rejectRoute(detailRouteId, reason);
    refreshDetail();
    refreshRoutes(id);
  }

  if (notFound) {
    return (
      <main className="fa-page">
        <EmptyState>Este aperturista no existe.</EmptyState>
        <Button as={Link} variant="ghost" small to="/first-ascensionists">
          Volver a aperturistas
        </Button>
      </main>
    );
  }

  if (!firstAscensionist) return null;

  return (
    <>
      <main className="fa-page">
        <nav className="area-breadcrumb" aria-label="Ruta de navegación">
          <Link to="/first-ascensionists">Aperturistas</Link>
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

        <RoutesList routes={routes} onSelect={setDetailRouteId} />
      </main>

      <DetailModal
        route={detailRoute}
        onClose={() => setDetailRouteId(null)}
        onEdit={() => {
          if (detailRouteId !== null) navigate(`/routes/${detailRouteId}/edit`);
        }}
        onDelete={handleDeleteRoute}
        onValidate={handleValidateRoute}
        onReject={handleRejectRoute}
        onAddTick={handleAddTick}
        onUploadTopo={handleUploadTopo}
        onDeleteTopo={handleDeleteTopo}
      />
    </>
  );
}
