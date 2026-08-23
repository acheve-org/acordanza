import { useState } from 'react';
import type { ReactNode } from 'react';
import { Button, EmptyState, Icon, List, ListRow } from 'acordanza-design-system';
import { RejectReasonModal } from './RejectReasonModal';

interface PendingValidationItem {
  id: number;
}

interface Props<T extends PendingValidationItem> {
  items: T[];
  emptyMessage: string;
  title: (item: T) => ReactNode;
  subtitle?: (item: T) => ReactNode;
  /** Row click navigates to a detail view instead of showing approve/reject controls (e.g. routes). */
  onSelect?: (item: T) => void;
  /** Approve/reject controls, shown instead of `onSelect`'s plain navigation. */
  onApprove?: (id: number) => Promise<unknown>;
  onReject?: (id: number, reason: string) => Promise<unknown>;
  rejectTitle?: (item: T) => string;
}

export function PendingValidationList<T extends PendingValidationItem>({
  items,
  emptyMessage,
  title,
  subtitle,
  onSelect,
  onApprove,
  onReject,
  rejectTitle,
}: Props<T>) {
  const [rejecting, setRejecting] = useState<T | null>(null);

  async function handleReject(reason: string) {
    if (!rejecting || !onReject) return;
    await onReject(rejecting.id, reason);
    setRejecting(null);
  }

  return (
    <>
      {items.length === 0 ? (
        <EmptyState>{emptyMessage}</EmptyState>
      ) : (
        <List>
          {items.map((item) => (
            <ListRow
              key={item.id}
              title={title(item)}
              subtitle={subtitle?.(item)}
              onClick={onSelect ? () => onSelect(item) : undefined}
              trailing={
                onApprove && onReject ? (
                  <>
                    <Button variant="ghost" small type="button" onClick={() => onApprove(item.id)}>
                      <Icon name="check" size="sm" /> Validar
                    </Button>
                    <Button variant="ghost" small danger type="button" onClick={() => setRejecting(item)}>
                      <Icon name="reject" size="sm" /> Rechazar
                    </Button>
                  </>
                ) : undefined
              }
            />
          ))}
        </List>
      )}

      {rejecting && rejectTitle && (
        <RejectReasonModal title={rejectTitle(rejecting)} onClose={() => setRejecting(null)} onReject={handleReject} />
      )}
    </>
  );
}
