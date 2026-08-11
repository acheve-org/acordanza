import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import type { CreateTickInput } from '../api/types';
import { errorMessage } from '../api/errorMessage';

interface Props {
  routeName: string;
  onClose: () => void;
  onSubmit: (input: CreateTickInput) => Promise<void>;
}

export function AddTickModal({ routeName, onClose, onSubmit }: Props) {
  const [date, setDate] = useState('');
  const [rating, setRating] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setSubmitting(true);
    try {
      await onSubmit({ date, rating: rating ? Number(rating) : null, notes: notes.trim() });
      onClose();
    } catch (err) {
      setError(errorMessage(err, 'No se pudo registrar el ascenso'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal onClose={onClose}>
      <h2>Registrar ascenso</h2>
      <p className="detail-meta">{routeName}</p>
      <form onSubmit={handleSubmit}>
        <div className="grid-3">
          <label>
            Fecha
            <input type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          <label>
            Puntuación <span className="hint">(1–5)</span>
            <input type="number" min={1} max={5} value={rating} onChange={(e) => setRating(e.target.value)} />
          </label>
        </div>
        <label>
          Notas
          <input
            type="text"
            placeholder="Encadenada al segundo intento, el paso clave fue la regleta…"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </label>
        <p className="error-text">{error}</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Registrando…' : 'Registrar'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
