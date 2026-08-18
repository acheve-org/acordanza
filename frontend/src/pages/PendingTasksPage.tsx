import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Badge, Button, EmptyState, List, ListRow } from 'acordanza-design-system';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { usePendingTasks } from '../hooks/usePendingTasks';
import { PendingAreaExpertRequests } from '../components/PendingAreaExpertRequests';
import { PendingFirstAscensionistLinkRequests } from '../components/PendingFirstAscensionistLinkRequests';
import { RejectReasonModal } from '../components/RejectReasonModal';
import type { AreaPending, FirstAscensionistPending, RoutePending } from '../api/types';
import { formatDate } from '../format';

type Tab = 'routes' | 'areas' | 'firstAscensionists' | 'areaExperts' | 'firstAscensionistLinks';

export function PendingTasksPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const {
    pendingRoutes,
    pendingAreas,
    pendingAreaExperts,
    pendingFirstAscensionistLinks,
    pendingFirstAscensionists,
    visible,
    showRoutes,
    showAreas,
    showAreaExperts,
    showFirstAscensionistLinks,
    showFirstAscensionists,
  } = usePendingTasks();

  const [rejectingArea, setRejectingArea] = useState<AreaPending | null>(null);
  const [rejectingFirstAscensionist, setRejectingFirstAscensionist] = useState<FirstAscensionistPending | null>(null);

  function invalidateAreaExperts() {
    queryClient.invalidateQueries({ queryKey: ['area-experts'] });
  }

  function invalidateFirstAscensionistLinks() {
    queryClient.invalidateQueries({ queryKey: ['first-ascensionist-links'] });
  }

  const validateAreaMutation = useMutation({
    mutationFn: (id: number) => areasApi.validateArea(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['areas'] }),
  });

  const rejectAreaMutation = useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => areasApi.rejectArea(id, reason),
    onSuccess: () => {
      setRejectingArea(null);
      queryClient.invalidateQueries({ queryKey: ['areas'] });
    },
  });

  const validateFirstAscensionistMutation = useMutation({
    mutationFn: (id: number) => firstAscensionistsApi.validateFirstAscensionist(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] }),
  });

  const rejectFirstAscensionistMutation = useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => firstAscensionistsApi.rejectFirstAscensionist(id, reason),
    onSuccess: () => {
      setRejectingFirstAscensionist(null);
      queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] });
    },
  });

  function handleSelectRoute(route: RoutePending) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

  const tabs: { key: Tab; label: string; count: number }[] = [
    ...(showRoutes ? [{ key: 'routes' as const, label: 'Vías', count: pendingRoutes.length }] : []),
    ...(showAreas ? [{ key: 'areas' as const, label: 'Sectores', count: pendingAreas.length }] : []),
    ...(showFirstAscensionists
      ? [{ key: 'firstAscensionists' as const, label: 'Aperturistas', count: pendingFirstAscensionists.length }]
      : []),
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
  ];

  // Permissions (and so tab availability) are stable for the life of this page, so a plain
  // useState initializer is enough — no effect needed to keep it in sync.
  const [activeTab, setActiveTab] = useState<Tab | null>(tabs[0]?.key ?? null);

  if (!visible) return <Navigate to="/" replace />;

  async function handleRejectArea(reason: string) {
    if (!rejectingArea) return;
    // RejectReasonModal shows its own error locally, so let the rejection propagate.
    await rejectAreaMutation.mutateAsync({ id: rejectingArea.id, reason });
  }

  async function handleRejectFirstAscensionist(reason: string) {
    if (!rejectingFirstAscensionist) return;
    // RejectReasonModal shows its own error locally, so let the rejection propagate.
    await rejectFirstAscensionistMutation.mutateAsync({ id: rejectingFirstAscensionist.id, reason });
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
                  subtitle={[r.areaPath, r.grade].filter(Boolean).join(' · ')}
                  onClick={() => handleSelectRoute(r)}
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
                      a.namePath
                    )
                  }
                  trailing={
                    <>
                      <Button variant="ghost" small type="button" onClick={() => validateAreaMutation.mutate(a.id)}>
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
          <PendingAreaExpertRequests items={pendingAreaExperts} onChange={invalidateAreaExperts} />
        </section>
      )}

      {activeTab === 'firstAscensionistLinks' && (
        <section className="settings-section">
          <PendingFirstAscensionistLinkRequests
            items={pendingFirstAscensionistLinks}
            onChange={invalidateFirstAscensionistLinks}
          />
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
                      ? `Creado por ${o.addedByName} el ${formatDate(o.createdAt)}`
                      : formatDate(o.createdAt)
                  }
                  trailing={
                    <>
                      <Button
                        variant="ghost"
                        small
                        type="button"
                        onClick={() => validateFirstAscensionistMutation.mutate(o.id)}
                      >
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
