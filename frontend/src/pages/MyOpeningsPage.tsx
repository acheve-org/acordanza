import { useEffect, useState } from 'react';
import { Navigate, useNavigate } from 'react-router';
import { RoutesList } from '../components/RoutesList';
import { DetailModal } from '../components/DetailModal';
import * as routesApi from '../api/routes';
import * as toposApi from '../api/topos';
import type { CreateTickInput, RouteDetail, RouteSummary } from '../api/types';
import { useAuth } from '../context/AuthContext';

const emptyFilters = { q: '', areaId: '', gear: '', tag: '', hasTopo: '' };

export function MyOpeningsPage() {
  const { id, isFirstAscensionist } = useAuth();
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [detailRoute, setDetailRoute] = useState<RouteDetail | null>(null);

  const navigate = useNavigate();

  function refreshRoutes() {
    routesApi.listRoutes(emptyFilters).then((all) => {
      setRoutes(all.filter((r) => r.firstAscensionists.some((fa) => fa.userId === id)));
    });
  }

  useEffect(refreshRoutes, [id]);

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

  if (!isFirstAscensionist) return <Navigate to="/my-acordanza" replace />;

  async function handleDeleteRoute() {
    if (!detailRouteId) return;
    await routesApi.deleteRoute(detailRouteId);
    setDetailRouteId(null);
    refreshRoutes();
  }

  async function handleAddTick(input: CreateTickInput) {
    if (!detailRouteId) return;
    await routesApi.createTick(detailRouteId, input);
    refreshDetail();
    refreshRoutes();
  }

  async function handleUploadTopo(file: File) {
    if (!detailRouteId) return;
    await toposApi.uploadTopo(detailRouteId, file);
    refreshDetail();
    refreshRoutes();
  }

  async function handleDeleteTopo(topoId: number) {
    await toposApi.deleteTopo(topoId);
    refreshDetail();
    refreshRoutes();
  }

  async function handleValidateRoute() {
    if (!detailRouteId) return;
    await routesApi.validateRoute(detailRouteId);
    refreshDetail();
    refreshRoutes();
  }

  return (
    <>
      <main className="layout">
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
        onAddTick={handleAddTick}
        onUploadTopo={handleUploadTopo}
        onDeleteTopo={handleDeleteTopo}
      />
    </>
  );
}
