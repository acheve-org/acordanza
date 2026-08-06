import { useState } from 'react';
import { Button, EmptyState, List, ListRow } from 'acordanza-design-system';
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
        <List>
          {items.map((e) => (
            <ListRow
              key={e.id}
              title={`${e.userName} · ${e.areaName}`}
              subtitle={`Solicitado el ${new Date(e.requestedAt).toLocaleDateString('es-ES')}`}
              trailing={
                <>
                  <Button variant="ghost" small type="button" onClick={() => handleApprove(e.id)}>
                    Aprobar
                  </Button>
                  <Button variant="ghost" small danger type="button" onClick={() => setRejecting(e)}>
                    Rechazar
                  </Button>
                </>
              }
            />
          ))}
        </List>
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
