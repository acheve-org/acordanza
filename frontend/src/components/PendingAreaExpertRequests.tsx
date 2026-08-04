import { useState } from 'react';
import { Button, EmptyState } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import type { AreaExpert } from '../api/types';
import { RejectReasonModal } from './RejectReasonModal';

interface Props {
  items: AreaExpert[];
  onChange: () => void;
}

export function PendingAreaExpertRequests({ items, onChange }: Props) {
  const [rejecting, setRejecting] = useState<AreaExpert | null>(null);

  async function handleApprove(id: number) {
    await areaExpertsApi.approveAreaExpert(id);
    onChange();
  }

  async function handleReject(reason: string) {
    if (!rejecting) return;
    await areaExpertsApi.rejectAreaExpert(rejecting.id, reason);
    setRejecting(null);
    onChange();
  }

  return (
    <>
      {items.length === 0 ? (
        <EmptyState>No hay solicitudes pendientes</EmptyState>
      ) : (
        <ul className="passkey-list">
          {items.map((e) => (
            <li key={e.id} className="tick-row">
              <div>
                <div className="tr-who">
                  {e.userName} · {e.areaName}
                </div>
                <div className="tr-notes">Solicitado el {new Date(e.requestedAt).toLocaleDateString('es-ES')}</div>
              </div>
              <div className="detail-actions">
                <Button variant="ghost" small type="button" onClick={() => handleApprove(e.id)}>
                  Aprobar
                </Button>
                <Button variant="ghost" small danger type="button" onClick={() => setRejecting(e)}>
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
