import { useState } from 'react';
import { Navigate } from 'react-router';
import { Button, EmptyState } from 'acordanza-design-system';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import { usePendingTasks } from '../context/PendingTasksContext';
import { PendingAreaExpertRequests } from '../components/PendingAreaExpertRequests';
import { PendingFirstAscensionistLinkRequests } from '../components/PendingFirstAscensionistLinkRequests';

type Tab = 'routes' | 'areas' | 'areaExperts' | 'firstAscensionistLinks';

export function PendingTasksPage() {
  const {
    pendingRoutes,
    pendingAreas,
    pendingAreaExperts,
    pendingFirstAscensionistLinks,
    visible,
    showRoutes,
    showAreas,
    showAreaExperts,
    showFirstAscensionistLinks,
    refresh,
  } = usePendingTasks();

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
  ];

  // Permissions (and so tab availability) are stable for the life of this page, so a plain
  // useState initializer is enough — no effect needed to keep it in sync.
  const [activeTab, setActiveTab] = useState<Tab | null>(tabs[0]?.key ?? null);

  if (!visible) return <Navigate to="/" replace />;

  async function handleValidateRoute(id: number) {
    await routesApi.validateRoute(id);
    refresh();
  }

  async function handleValidateArea(id: number) {
    await areasApi.validateArea(id);
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
            {t.count > 0 && <span className="tab-badge">{t.count > 99 ? '99+' : t.count}</span>}
          </button>
        ))}
      </div>

      {activeTab === 'routes' && (
        <section className="settings-section">
          {pendingRoutes.length === 0 ? (
            <EmptyState>No hay vías pendientes de validación</EmptyState>
          ) : (
            <ul className="passkey-list">
              {pendingRoutes.map((r) => (
                <li key={r.id} className="tick-row">
                  <div>
                    <div className="tr-who">{r.name}</div>
                    <div className="tr-notes">
                      {r.areaPath} · {r.grade}
                    </div>
                  </div>
                  <Button variant="ghost" small type="button" onClick={() => handleValidateRoute(r.id)}>
                    Validar
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {activeTab === 'areas' && (
        <section className="settings-section">
          {pendingAreas.length === 0 ? (
            <EmptyState>No hay sectores pendientes de validación</EmptyState>
          ) : (
            <ul className="passkey-list">
              {pendingAreas.map((a) => (
                <li key={a.id} className="tick-row">
                  <div className="tr-who">{a.name}</div>
                  <Button variant="ghost" small type="button" onClick={() => handleValidateArea(a.id)}>
                    Validar
                  </Button>
                </li>
              ))}
            </ul>
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
    </>
  );
}
