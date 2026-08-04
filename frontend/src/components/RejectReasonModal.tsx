import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import { ApiError } from '../api/client';

interface Props {
  title: string;
  onClose: () => void;
  onReject: (reason: string) => Promise<unknown>;
}

export function RejectReasonModal({ title, onClose, onReject }: Props) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!reason.trim()) return;
    setError('');
    setSubmitting(true);
    try {
      await onReject(reason.trim());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo rechazar la solicitud');
      setSubmitting(false);
    }
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
            {submitting ? 'Rechazando…' : 'Rechazar solicitud'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
