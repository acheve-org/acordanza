import { useState } from 'react';
import { Button, EmptyState, List, ListRow } from 'acordanza-design-system';
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
        <List>
          {items.map((r) => (
            <ListRow
              key={r.id}
              title={`${r.userName} · ${r.firstAscensionistName}`}
              subtitle={`Solicitado el ${new Date(r.requestedAt).toLocaleDateString('es-ES')}`}
              trailing={
                <>
                  <Button variant="ghost" small type="button" onClick={() => handleApprove(r.id)}>
                    Aprobar
                  </Button>
                  <Button variant="ghost" small danger type="button" onClick={() => setRejecting(r)}>
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
