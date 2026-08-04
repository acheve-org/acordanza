import { useState } from 'react';
import { Button, EmptyState } from 'acordanza-design-system';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import type { FirstAscensionistLinkRequestSummary } from '../api/types';
import { RejectReasonModal } from './RejectReasonModal';

interface Props {
  items: FirstAscensionistLinkRequestSummary[];
  onChange: () => void;
}

export function PendingFirstAscensionistLinkRequests({ items, onChange }: Props) {
  const [rejecting, setRejecting] = useState<FirstAscensionistLinkRequestSummary | null>(null);

  async function handleApprove(id: number) {
    await firstAscensionistLinksApi.approveFirstAscensionistLink(id);
    onChange();
  }

  async function handleReject(reason: string) {
    if (!rejecting) return;
    await firstAscensionistLinksApi.rejectFirstAscensionistLink(rejecting.id, reason);
    setRejecting(null);
    onChange();
  }

  return (
    <>
      {items.length === 0 ? (
        <EmptyState>No hay solicitudes pendientes</EmptyState>
      ) : (
        <ul className="passkey-list">
          {items.map((r) => (
            <li key={r.id} className="tick-row">
              <div>
                <div className="tr-who">
                  {r.userName} · {r.firstAscensionistName}
                </div>
                <div className="tr-notes">Solicitado el {new Date(r.requestedAt).toLocaleDateString('es-ES')}</div>
              </div>
              <div className="detail-actions">
                <Button variant="ghost" small type="button" onClick={() => handleApprove(r.id)}>
                  Aprobar
                </Button>
                <Button variant="ghost" small danger type="button" onClick={() => setRejecting(r)}>
                  Rechazar
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {rejecting && (
        <RejectReasonModal
          title={`Rechazar solicitud de ${rejecting.userName}`}
          onClose={() => setRejecting(null)}
          onReject={handleReject}
        />
      )}
    </>
  );
}
