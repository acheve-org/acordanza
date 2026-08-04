import { Link, Navigate } from 'react-router';
import { Button, EmptyState } from 'acordanza-design-system';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import { usePendingTasks } from '../context/PendingTasksContext';
import { PendingAreaExpertRequests } from '../components/PendingAreaExpertRequests';
import { PendingFirstAscensionistLinkRequests } from '../components/PendingFirstAscensionistLinkRequests';

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
    <main className="settings-page">
      <h1>Tareas pendientes</h1>

      {showRoutes && (
        <section className="settings-section">
          <h2>Vías pendientes de validación</h2>
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

      {showAreas && (
        <section className="settings-section">
          <h2>Sectores pendientes de validación</h2>
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

      {showAreaExperts && (
        <section className="settings-section">
          <h2>Solicitudes de expertos de sector</h2>
          <PendingAreaExpertRequests items={pendingAreaExperts} onChange={refresh} />
        </section>
      )}

      {showFirstAscensionistLinks && (
        <section className="settings-section">
          <h2>Solicitudes de vínculo de aperturista</h2>
          <PendingFirstAscensionistLinkRequests items={pendingFirstAscensionistLinks} onChange={refresh} />
        </section>
      )}

      <Button as={Link} variant="ghost" small to="/">
        Volver
      </Button>
    </main>
  );
}
