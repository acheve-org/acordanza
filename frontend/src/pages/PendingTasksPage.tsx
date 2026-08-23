import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Badge } from 'acordanza-design-system';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { usePendingTasks } from '../hooks/usePendingTasks';
import { PendingAreaExpertRequests } from '../components/PendingAreaExpertRequests';
import { PendingFirstAscensionistLinkRequests } from '../components/PendingFirstAscensionistLinkRequests';
import { PendingValidationList } from '../components/PendingValidationList';
import type { RoutePending } from '../api/types';
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
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['areas'] }),
  });

  const validateFirstAscensionistMutation = useMutation({
    mutationFn: (id: number) => firstAscensionistsApi.validateFirstAscensionist(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] }),
  });

  const rejectFirstAscensionistMutation = useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => firstAscensionistsApi.rejectFirstAscensionist(id, reason),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] }),
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
          <PendingValidationList
            items={pendingRoutes}
            emptyMessage="No hay vías pendientes de validación"
            title={(r) => r.name}
            subtitle={(r) => [r.areaPath, r.grade].filter(Boolean).join(' · ')}
            onSelect={handleSelectRoute}
          />
        </section>
      )}

      {activeTab === 'areas' && (
        <section className="settings-section">
          <PendingValidationList
            items={pendingAreas}
            emptyMessage="No hay sectores pendientes de validación"
            title={(a) => a.name}
            subtitle={(a) =>
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
            onApprove={(id) => validateAreaMutation.mutateAsync(id)}
            onReject={(id, reason) => rejectAreaMutation.mutateAsync({ id, reason })}
            rejectTitle={(a) => `Rechazar "${a.name}"`}
          />
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
          <PendingValidationList
            items={pendingFirstAscensionists}
            emptyMessage="No hay aperturistas pendientes de validación"
            title={(o) => o.name}
            subtitle={(o) => (o.addedByName ? `Creado por ${o.addedByName} el ${formatDate(o.createdAt)}` : formatDate(o.createdAt))}
            onApprove={(id) => validateFirstAscensionistMutation.mutateAsync(id)}
            onReject={(id, reason) => rejectFirstAscensionistMutation.mutateAsync({ id, reason })}
            rejectTitle={(o) => `Rechazar "${o.name}"`}
          />
        </section>
      )}
    </>
  );
}
