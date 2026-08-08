import { useEffect, useState } from 'react';
import { Navigate, useNavigate } from 'react-router';
import { Badge, Button, EmptyState, List, ListRow } from 'acordanza-design-system';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import { areaPath } from '../api/areas';
import * as toposApi from '../api/topos';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { usePendingTasks } from '../context/PendingTasksContext';
import { PendingAreaExpertRequests } from '../components/PendingAreaExpertRequests';
import { PendingFirstAscensionistLinkRequests } from '../components/PendingFirstAscensionistLinkRequests';
import { DetailModal } from '../components/DetailModal';
import { RejectReasonModal } from '../components/RejectReasonModal';
import type { AreaSummary, CreateTickInput, FirstAscensionistPending, RouteDetail } from '../api/types';

type Tab = 'routes' | 'areas' | 'areaExperts' | 'firstAscensionistLinks' | 'firstAscensionists';

export function PendingTasksPage() {
  const navigate = useNavigate();
  const {
    pendingRoutes,
    pendingAreas,
    areasById,
    pendingAreaExperts,
    pendingFirstAscensionistLinks,
    pendingFirstAscensionists,
    visible,
    showRoutes,
    showAreas,
    showAreaExperts,
    showFirstAscensionistLinks,
    showFirstAscensionists,
    refresh,
  } = usePendingTasks();

  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [detailRoute, setDetailRoute] = useState<RouteDetail | null>(null);
  const [rejectingArea, setRejectingArea] = useState<AreaSummary | null>(null);
  const [rejectingFirstAscensionist, setRejectingFirstAscensionist] = useState<FirstAscensionistPending | null>(null);

  useEffect(() => {
    if (detailRouteId === null) {
      setDetailRoute(null);
      return;
    }
    let cancelled = false;
    routesApi.getRoute(detailRouteId).then((route) => {
      if (!cancelled) setDetailRoute(route);
    });
    return () => {
      cancelled = true;
    };
  }, [detailRouteId]);

  function refreshDetail() {
    if (detailRouteId !== null) routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }

  const tabs: { key: Tab; label: string; count: number }[] = [
    ...(showRoutes ? [{ key: 'routes' as const, label: 'Vías', count: pendingRoutes.length }] : []),
    ...(showAreas ? [{ key: 'areas' as const, label: 'Sectores', count: pendingAreas.length }] : []),
    ...(showAreaExperts
      ? [{ key: 'areaExperts' as const, label: 'Expertos de sector', count: pendingAreaExperts.length }]
      : []),
    ...(showFirstAscensionistLinks
      ? [
          {
            key: 'firstAscensionistLinks' as const,
            label: 'Vínculos de aperturista',
            count: pendingFirstAscensionistLinks.length,
          },
        ]
      : []),
    ...(showFirstAscensionists
      ? [{ key: 'firstAscensionists' as const, label: 'Aperturistas', count: pendingFirstAscensionists.length }]
      : []),
  ];

  // Permissions (and so tab availability) are stable for the life of this page, so a plain
  // useState initializer is enough — no effect needed to keep it in sync.
  const [activeTab, setActiveTab] = useState<Tab | null>(tabs[0]?.key ?? null);

  if (!visible) return <Navigate to="/" replace />;

  async function handleValidateRoute() {
    if (detailRouteId === null) return;
    await routesApi.validateRoute(detailRouteId);
    refreshDetail();
    refresh();
  }

  async function handleRejectRoute(reason: string) {
    if (detailRouteId === null) return;
    await routesApi.rejectRoute(detailRouteId, reason);
    refreshDetail();
    refresh();
  }

  async function handleDeleteRoute() {
    if (detailRouteId === null) return;
    await routesApi.deleteRoute(detailRouteId);
    setDetailRouteId(null);
    refresh();
  }

  async function handleAddTick(input: CreateTickInput) {
    if (detailRouteId === null) return;
    await routesApi.createTick(detailRouteId, input);
    refreshDetail();
    refresh();
  }

  async function handleUploadTopo(file: File) {
    if (detailRouteId === null) return;
    await toposApi.uploadTopo(detailRouteId, file);
    refreshDetail();
  }

  async function handleDeleteTopo(topoId: number) {
    await toposApi.deleteTopo(topoId);
    refreshDetail();
  }

  async function handleValidateArea(id: number) {
    await areasApi.validateArea(id);
    refresh();
  }

  async function handleRejectArea(reason: string) {
    if (!rejectingArea) return;
    await areasApi.rejectArea(rejectingArea.id, reason);
    setRejectingArea(null);
    refresh();
  }

  async function handleValidateFirstAscensionist(id: number) {
    await firstAscensionistsApi.validateFirstAscensionist(id);
    refresh();
  }

  async function handleRejectFirstAscensionist(reason: string) {
    if (!rejectingFirstAscensionist) return;
    await firstAscensionistsApi.rejectFirstAscensionist(rejectingFirstAscensionist.id, reason);
    setRejectingFirstAscensionist(null);
    refresh();
  }

  return (
    <>
      <h1>Tareas pendientes</h1>

      <div className="tab-bar">
        {tabs.map((t) => (
          <button
            key={t.key}
            type="button"
            className={`tab-item${activeTab === t.key ? ' active' : ''}`}
            onClick={() => setActiveTab(t.key)}
          >
            {t.label}
            <Badge count={t.count} />
          </button>
        ))}
      </div>

      {activeTab === 'routes' && (
        <section className="settings-section">
          {pendingRoutes.length === 0 ? (
            <EmptyState>No hay vías pendientes de validación</EmptyState>
          ) : (
            <List>
              {pendingRoutes.map((r) => (
                <ListRow
                  key={r.id}
                  title={r.name}
                  subtitle={`${r.areaPath} · ${r.grade}`}
                  onClick={() => setDetailRouteId(r.id)}
                />
              ))}
            </List>
          )}
        </section>
      )}

      {activeTab === 'areas' && (
        <section className="settings-section">
          {pendingAreas.length === 0 ? (
            <EmptyState>No hay sectores pendientes de validación</EmptyState>
          ) : (
            <List>
              {pendingAreas.map((a) => (
                <ListRow
                  key={a.id}
                  title={a.name}
                  subtitle={
                    a.parentAreaId === null ? (
                      <span className="area-root-badge">
                        <svg
                          className="area-root-icon"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          aria-label="Sector principal"
                        >
                          <title>Sector principal</title>
                          <path d="M3 10.5 12 4l9 6.5" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" />
                          <path d="M5 9.5V19a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9.5" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" />
                        </svg>
                        Sector principal
                      </span>
                    ) : (
                      areaPath(a, areasById)
                    )
                  }
                  trailing={
                    <>
                      <Button variant="ghost" small type="button" onClick={() => handleValidateArea(a.id)}>
                        Validar
                      </Button>
                      <Button variant="ghost" small danger type="button" onClick={() => setRejectingArea(a)}>
                        Rechazar
                      </Button>
                    </>
                  }
                />
              ))}
            </List>
          )}
        </section>
      )}

      {activeTab === 'areaExperts' && (
        <section className="settings-section">
          <PendingAreaExpertRequests items={pendingAreaExperts} onChange={refresh} />
        </section>
      )}

      {activeTab === 'firstAscensionistLinks' && (
        <section className="settings-section">
          <PendingFirstAscensionistLinkRequests items={pendingFirstAscensionistLinks} onChange={refresh} />
        </section>
      )}

      {activeTab === 'firstAscensionists' && (
        <section className="settings-section">
          {pendingFirstAscensionists.length === 0 ? (
            <EmptyState>No hay aperturistas pendientes de validación</EmptyState>
          ) : (
            <List>
              {pendingFirstAscensionists.map((o) => (
                <ListRow
                  key={o.id}
                  title={o.name}
                  subtitle={
                    o.addedByName
                      ? `Creado por ${o.addedByName} el ${new Date(o.createdAt).toLocaleDateString()}`
                      : new Date(o.createdAt).toLocaleDateString()
                  }
                  trailing={
                    <>
                      <Button variant="ghost" small type="button" onClick={() => handleValidateFirstAscensionist(o.id)}>
                        Validar
                      </Button>
                      <Button variant="ghost" small danger type="button" onClick={() => setRejectingFirstAscensionist(o)}>
                        Rechazar
                      </Button>
                    </>
                  }
                />
              ))}
            </List>
          )}
        </section>
      )}

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

      {rejectingArea && (
        <RejectReasonModal
          title={`Rechazar "${rejectingArea.name}"`}
          onClose={() => setRejectingArea(null)}
          onReject={handleRejectArea}
        />
      )}

      {rejectingFirstAscensionist && (
        <RejectReasonModal
          title={`Rechazar "${rejectingFirstAscensionist.name}"`}
          onClose={() => setRejectingFirstAscensionist(null)}
          onReject={handleRejectFirstAscensionist}
        />
      )}
    </>
  );
}
