import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { Filters } from '../components/Filters';
import { RoutesList } from '../components/RoutesList';
import { DetailModal } from '../components/DetailModal';
import * as routesApi from '../api/routes';
import * as toposApi from '../api/topos';
import type { CreateTickInput, RouteDetail, RouteFilters, RouteSummary } from '../api/types';

const emptyFilters: RouteFilters = { q: '', areaId: '', gear: '', tag: '', hasTopo: '' };

export function MainView() {
  const navigate = useNavigate();
  const [filters, setFilters] = useState<RouteFilters>(emptyFilters);
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [detailRoute, setDetailRoute] = useState<RouteDetail | null>(null);

  useEffect(() => {
    const handle = setTimeout(() => {
      routesApi.listRoutes(filters).then(setRoutes);
    }, 250);
    return () => clearTimeout(handle);
  }, [filters]);

  useEffect(() => {
    if (detailRouteId === null) {
      setDetailRoute(null);
      return;
    }
    routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }, [detailRouteId]);

  function refreshRoutes() {
    routesApi.listRoutes(filters).then(setRoutes);
  }

  function refreshDetail() {
    if (detailRouteId !== null) routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }

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
        <Filters filters={filters} onChange={setFilters} onAddRoute={() => navigate('/routes/new')} />
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
