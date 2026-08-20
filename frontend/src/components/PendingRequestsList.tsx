import { useState } from 'react';
import { Button, EmptyState, Icon, List, ListRow } from 'acordanza-design-system';
import { RejectReasonModal } from './RejectReasonModal';
import { formatDate } from '../format';

interface PendingRequestItem {
  id: number;
  userName: string;
  requestedAt: string;
}

interface Props<T extends PendingRequestItem> {
  items: T[];
  subjectLabel: string;
  subjectName: (item: T) => string;
  onApprove: (id: number) => Promise<unknown>;
  onReject: (id: number, reason: string) => Promise<unknown>;
  onChange: () => void;
}

export function PendingRequestsList<T extends PendingRequestItem>({
  items,
  subjectLabel,
  subjectName,
  onApprove,
  onReject,
  onChange,
}: Props<T>) {
  const [rejecting, setRejecting] = useState<T | null>(null);

  async function handleApprove(id: number) {
    await onApprove(id);
    onChange();
  }

  async function handleReject(reason: string) {
    if (!rejecting) return;
    await onReject(rejecting.id, reason);
    setRejecting(null);
    onChange();
  }

  return (
    <>
      {items.length === 0 ? (
        <EmptyState>No hay solicitudes pendientes</EmptyState>
      ) : (
        <List>
          {items.map((item) => (
            <ListRow
              key={item.id}
              title={
                <>
                  <div className="tr-field">
                    <span className="tr-field-label">Usuario</span>
                    <span>{item.userName}</span>
                  </div>
                  <div className="tr-field">
                    <span className="tr-field-label">{subjectLabel}</span>
                    <span>{subjectName(item)}</span>
                  </div>
                </>
              }
              subtitle={`Solicitado el ${formatDate(item.requestedAt)}`}
              trailing={
                <>
                  <Button variant="ghost" small type="button" onClick={() => handleApprove(item.id)}>
                    <Icon name="check" size="sm" /> Aprobar
                  </Button>
                  <Button variant="ghost" small danger type="button" onClick={() => setRejecting(item)}>
                    <Icon name="reject" size="sm" /> Rechazar
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
