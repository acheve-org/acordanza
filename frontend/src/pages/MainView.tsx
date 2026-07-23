import { useEffect, useState } from 'react';
import { Filters } from '../components/Filters';
import { RoutesList } from '../components/RoutesList';
import { RouteFormModal } from '../components/RouteFormModal';
import { DetailModal } from '../components/DetailModal';
import * as routesApi from '../api/routes';
import type { CreateTickInput, RouteDetail, RouteFilters, RouteSummary, UpsertRouteInput } from '../api/types';

const emptyFilters: RouteFilters = { q: '', crag: '', type: '', tag: '' };

export function MainView() {
  const [filters, setFilters] = useState<RouteFilters>(emptyFilters);
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const [routeModalOpen, setRouteModalOpen] = useState(false);
  const [editingRoute, setEditingRoute] = useState<RouteSummary | RouteDetail | null>(null);

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

  async function handleSaveRoute(input: UpsertRouteInput) {
    if (editingRoute) {
      await routesApi.updateRoute(editingRoute.id, input);
    } else {
      await routesApi.createRoute(input);
    }
    setRouteModalOpen(false);
    setEditingRoute(null);
    refreshRoutes();
    refreshDetail();
  }

  async function handleDeleteRoute() {
    if (!detailRouteId) return;
    if (!confirm('¿Eliminar esta vía y todos sus ascensos registrados?')) return;
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

  async function handleDeleteTick(tickId: number) {
    await routesApi.deleteTick(tickId);
    refreshDetail();
  }

  return (
    <>
      <main className="layout">
        <Filters
          filters={filters}
          onChange={setFilters}
          onAddRoute={() => {
            setEditingRoute(null);
            setRouteModalOpen(true);
          }}
        />
        <RoutesList routes={routes} onSelect={setDetailRouteId} />
      </main>

      <RouteFormModal
        open={routeModalOpen}
        editing={editingRoute}
        onCancel={() => {
          setRouteModalOpen(false);
          setEditingRoute(null);
        }}
        onSave={handleSaveRoute}
      />

      <DetailModal
        route={detailRoute}
        onClose={() => setDetailRouteId(null)}
        onEdit={() => {
          setEditingRoute(detailRoute);
          setDetailRouteId(null);
          setRouteModalOpen(true);
        }}
        onDelete={handleDeleteRoute}
        onAddTick={handleAddTick}
        onDeleteTick={handleDeleteTick}
      />
    </>
  );
}
