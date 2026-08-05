import { useEffect, useState } from 'react';
import { Button, EmptyState } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import * as areasApi from '../api/areas';
import type { AreaExpert, AreaSummary } from '../api/types';
import { ApiError } from '../api/client';
import { RequestAreaExpertModal } from '../components/RequestAreaExpertModal';

const statusLabels: Record<AreaExpert['status'], string> = {
  pending: 'Solicitud pendiente de aprobación',
  approved: 'Experto aprobado',
  rejected: 'Solicitud rechazada',
};

export function MyAreasPage() {
  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [mine, setMine] = useState<AreaExpert[]>([]);
  const [error, setError] = useState('');
  const [requesting, setRequesting] = useState(false);

  function refresh() {
    areasApi.listAreas().then(setAreas);
    areaExpertsApi
      .listMyAreaExperts()
      .then(setMine)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'No se pudieron cargar tus sectores'));
  }

  useEffect(refresh, []);

  const approvedAreas = mine.filter((e) => e.status === 'approved');
  const activeRequestAreaIds = new Set(mine.filter((e) => e.status !== 'rejected').map((e) => e.areaId));
  const availableAreas = areas.filter((a) => a.parentAreaId === null && !activeRequestAreaIds.has(a.id));

  async function handleWithdraw(id: number) {
    try {
      await areaExpertsApi.cancelAreaExpert(id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo retirar la solicitud');
    }
    refresh();
  }

  function handleRequested() {
    setRequesting(false);
    refresh();
  }

  return (
    <>
      <h1>Mis sectores</h1>

      {approvedAreas.length > 0 && (
        <div className="expert-banner">
          <svg className="expert-banner-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
            <path
              d="M12 3l7 3v5c0 4.5-3 8.3-7 10-4-1.7-7-5.5-7-10V6l7-3z"
              strokeLinejoin="round"
            />
            <path d="M9 12l2 2 4-4" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <div>
            <h2>Eres experto de {approvedAreas.length === 1 ? 'un sector' : `${approvedAreas.length} sectores`}</h2>
            <p>
              Como experto de <strong>{approvedAreas.map((e) => e.areaName).join(', ')}</strong> puedes validar las
              vías de ese sector y de sus subsectores desde la ficha de cada vía.
            </p>
          </div>
        </div>
      )}

      <section className="settings-section">
        {mine.length === 0 ? (
          <EmptyState>Todavía no eres experto de ningún sector</EmptyState>
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
          <Button type="button" onClick={() => setRequesting(true)}>
            Solicitar acceso
          </Button>
        )}
        {error && <p className="error-text">{error}</p>}
      </section>

      {requesting && (
        <RequestAreaExpertModal areas={availableAreas} onClose={() => setRequesting(false)} onRequested={handleRequested} />
      )}
    </>
  );
}
