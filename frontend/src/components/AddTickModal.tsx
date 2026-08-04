import { useState, type FormEvent } from 'react';
import type { CreateTickInput } from '../api/types';
import { ApiError } from '../api/client';

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
      setError(err instanceof ApiError ? err.message : 'No se pudo registrar el ascenso');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal">
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
          <div className="modal-actions">
            <button type="button" className="ghost-btn" onClick={onClose}>
              Cancelar
            </button>
            <button type="submit" className="primary-btn" disabled={submitting}>
              {submitting ? 'Registrando…' : 'Registrar'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
