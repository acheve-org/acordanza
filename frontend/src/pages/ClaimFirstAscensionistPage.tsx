import { useEffect, useState } from 'react';
import { Button, EmptyState } from 'acordanza-design-system';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import type { FirstAscensionistLinkRequestSummary } from '../api/types';
import { ApiError } from '../api/client';
import { RequestFirstAscensionistLinkModal } from '../components/RequestFirstAscensionistLinkModal';

const statusLabels: Record<FirstAscensionistLinkRequestSummary['status'], string> = {
  pending: 'Pendiente de aprobación',
  approved: 'Aprobado',
  rejected: 'Rechazado',
};

export function ClaimFirstAscensionistPage() {
  const [mine, setMine] = useState<FirstAscensionistLinkRequestSummary[]>([]);
  const [error, setError] = useState('');
  const [requesting, setRequesting] = useState(false);

  function refresh() {
    firstAscensionistLinksApi
      .listMyFirstAscensionistLinks()
      .then(setMine)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'No se pudieron cargar tus solicitudes'));
  }

  useEffect(refresh, []);

  const activeRequestFirstAscensionistIds = mine.filter((r) => r.status !== 'rejected').map((r) => r.firstAscensionistId);

  async function handleCancel(id: number) {
    try {
      await firstAscensionistLinksApi.cancelFirstAscensionistLink(id);
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
      <h1>Vincular aperturista</h1>

      <section className="settings-section">
        {mine.length === 0 ? (
          <EmptyState>Todavía no has solicitado ningún vínculo</EmptyState>
        ) : (
          <ul className="passkey-list">
            {mine.map((r) => (
              <li key={r.id} className="tick-row">
                <div>
                  <div className="tr-who">{r.firstAscensionistName}</div>
                  <div className="tr-notes">
                    {statusLabels[r.status]}
                    {r.status !== 'pending' && r.decidedByName && ` · ${r.decidedByName}`}
                    {r.status === 'rejected' && r.rejectionReason && ` · Motivo: ${r.rejectionReason}`}
                  </div>
                </div>
                {r.status === 'pending' && (
                  <button
                    type="button"
                    className="tick-remove"
                    aria-label="Retirar solicitud"
                    onClick={() => handleCancel(r.id)}
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
        <h2>Solicitar vínculo</h2>
        <Button type="button" onClick={() => setRequesting(true)}>
          Solicitar vínculo
        </Button>
        {error && <p className="error-text">{error}</p>}
      </section>

      {requesting && (
        <RequestFirstAscensionistLinkModal
          excludedFirstAscensionistIds={activeRequestFirstAscensionistIds}
          onClose={() => setRequesting(false)}
          onRequested={handleRequested}
        />
      )}
    </>
  );
}
