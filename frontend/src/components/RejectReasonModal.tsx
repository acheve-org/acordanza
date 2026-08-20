import { useState, type FormEvent } from 'react';
import { Button, Icon, Modal, ModalActions } from 'acordanza-design-system';
import { useSubmit } from '../hooks/useSubmit';

interface Props {
  title: string;
  onClose: () => void;
  onReject: (reason: string) => Promise<unknown>;
}

export function RejectReasonModal({ title, onClose, onReject }: Props) {
  const [reason, setReason] = useState('');
  const { error, submitting, submit } = useSubmit('No se pudo rechazar la solicitud');

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!reason.trim()) return;
    await submit(() => onReject(reason.trim()));
  }

  return (
    <Modal onClose={onClose}>
      <h2>{title}</h2>
      <form onSubmit={handleSubmit}>
        <label>
          Motivo del rechazo
          <textarea required rows={3} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
        </label>
        <p className="error-text">{error}</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={submitting || !reason.trim()}>
            <Icon name="reject" size="sm" /> {submitting ? 'Rechazando…' : 'Rechazar solicitud'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
