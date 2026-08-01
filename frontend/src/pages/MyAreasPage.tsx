import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import * as areaExpertsApi from '../api/areaExperts';
import * as areasApi from '../api/areas';
import type { AreaExpert, AreaSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { RejectReasonModal } from '../components/RejectReasonModal';

const statusLabels: Record<AreaExpert['status'], string> = {
  pending: 'Solicitud pendiente de aprobación',
  approved: 'Experto aprobado',
  rejected: 'Solicitud rechazada',
};

export function MyAreasPage() {
  const { hasPermission } = useAuth();
  const canApprove = hasPermission(Permissions.areaExpertApprove);

  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [mine, setMine] = useState<AreaExpert[]>([]);
  const [pending, setPending] = useState<AreaExpert[]>([]);
  const [selectedAreaId, setSelectedAreaId] = useState('');
  const [error, setError] = useState('');
  const [rejecting, setRejecting] = useState<AreaExpert | null>(null);

  function refresh() {
    areasApi.listAreas().then(setAreas);
    areaExpertsApi
      .listMyAreaExperts()
      .then(setMine)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'No se pudieron cargar tus sectores'));
    if (canApprove) {
      areaExpertsApi.listPendingAreaExperts().then(setPending);
    }
  }

  useEffect(refresh, [canApprove]);

  const activeRequestAreaIds = new Set(mine.filter((e) => e.status !== 'rejected').map((e) => e.areaId));
  const availableAreas = areas.filter((a) => a.parentAreaId === null && !activeRequestAreaIds.has(a.id));

  async function handleRequest(e: FormEvent) {
    e.preventDefault();
    if (!selectedAreaId) return;
    setError('');
    try {
      await areaExpertsApi.requestAreaExpert(Number(selectedAreaId));
      setSelectedAreaId('');
      refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo solicitar el acceso');
    }
  }

  async function handleWithdraw(id: number) {
    try {
      await areaExpertsApi.cancelAreaExpert(id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo retirar la solicitud');
    }
    refresh();
  }

  async function handleApprove(id: number) {
    await areaExpertsApi.approveAreaExpert(id);
    refresh();
  }

  async function handleReject(reason: string) {
    if (!rejecting) return;
    await areaExpertsApi.rejectAreaExpert(rejecting.id, reason);
    setRejecting(null);
    refresh();
  }

  return (
    <main className="settings-page">
      <h1>Mis sectores</h1>

      <section className="settings-section">
        {mine.length === 0 ? (
          <p className="empty-state">Todavía no eres experto de ningún sector</p>
        ) : (
          <ul className="passkey-list">
            {mine.map((e) => (
              <li key={e.id} className="tick-row">
                <div>
                  <div className="tr-who">{e.areaName}</div>
                  <div className="tr-notes">
                    {statusLabels[e.status]}
                    {e.status !== 'pending' && e.decidedByName && ` · ${e.decidedByName}`}
                    {e.status === 'rejected' && e.rejectionReason && ` · Motivo: ${e.rejectionReason}`}
                  </div>
                </div>
                {e.status === 'pending' && (
                  <button
                    type="button"
                    className="tick-remove"
                    aria-label="Retirar solicitud"
                    onClick={() => handleWithdraw(e.id)}
                  >
                    &times;
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="settings-section">
        <h2>Solicitar acceso a un sector</h2>
        {availableAreas.length === 0 ? (
          <p className="hint">No quedan sectores disponibles para solicitar.</p>
        ) : (
          <form onSubmit={handleRequest} className="passkey-form">
            <label>
              Sector
              <select value={selectedAreaId} onChange={(e) => setSelectedAreaId(e.target.value)}>
                <option value="">Selecciona un sector…</option>
                {availableAreas.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit" className="primary-btn" disabled={!selectedAreaId}>
              Solicitar
            </button>
          </form>
        )}
        {error && <p className="error-text">{error}</p>}
      </section>

      {canApprove && (
        <section className="settings-section">
          <h2>Solicitudes pendientes</h2>
          {pending.length === 0 ? (
            <p className="empty-state">No hay solicitudes pendientes</p>
          ) : (
            <ul className="passkey-list">
              {pending.map((e) => (
                <li key={e.id} className="tick-row">
                  <div>
                    <div className="tr-who">
                      {e.userName} · {e.areaName}
                    </div>
                    <div className="tr-notes">Solicitado el {new Date(e.requestedAt).toLocaleDateString('es-ES')}</div>
                  </div>
                  <div className="detail-actions">
                    <button type="button" className="ghost-btn small" onClick={() => handleApprove(e.id)}>
                      Aprobar
                    </button>
                    <button type="button" className="ghost-btn small danger" onClick={() => setRejecting(e)}>
                      Rechazar
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {rejecting && (
        <RejectReasonModal
          title={`Rechazar solicitud de ${rejecting.userName}`}
          onClose={() => setRejecting(null)}
          onReject={handleReject}
        />
      )}

      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
  );
}
