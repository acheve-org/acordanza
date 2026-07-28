import { useState, type FormEvent } from 'react';
import type { CreateTickInput, RouteDetail } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';

interface Props {
  route: RouteDetail | null;
  onClose: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onAddTick: (input: CreateTickInput) => Promise<void>;
  onDeleteTick: (tickId: number) => Promise<void>;
}

export function DetailModal({ route, onClose, onEdit, onDelete, onAddTick, onDeleteTick }: Props) {
  const [date, setDate] = useState('');
  const [rating, setRating] = useState('');
  const [notes, setNotes] = useState('');

  if (!route) return null;

  const tags = route.tags
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean);

  async function handleTickSubmit(e: FormEvent) {
    e.preventDefault();
    await onAddTick({ date, rating: rating ? Number(rating) : null, notes: notes.trim() });
    setDate('');
    setRating('');
    setNotes('');
  }

  return (
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal">
        <div className="detail-head">
          <div>
            <h2>{route.name}</h2>
            <p className="detail-meta">
              {route.grade} · {route.cragPath}
            </p>
            <p className="detail-fa">
              {[route.orientation && orientationLabels[route.orientation], route.gear && gearLabels[route.gear]]
                .filter(Boolean)
                .join(' · ')}
            </p>
          </div>
          <div className="detail-actions">
            <button className="ghost-btn small" onClick={onEdit}>
              Editar
            </button>
            <button className="ghost-btn small danger" onClick={onDelete}>
              Eliminar
            </button>
          </div>
        </div>
        <p className="detail-desc">{route.description || 'Sin descripción todavía.'}</p>
        {route.openers.length > 0 && (
          <p className="detail-fa">
            Apertura:{' '}
            {route.openers
              .map((o) => (o.userName ? `${o.name} (${o.userName})` : o.name))
              .join(', ')}
          </p>
        )}
        <div className="tag-row">
          {tags.map((t) => (
            <span key={t} className="tag-chip">
              {t}
            </span>
          ))}
        </div>

        <hr className="divider" />

        <h3>Registrar un ascenso</h3>
        <form className="tick-form" onSubmit={handleTickSubmit}>
          <div className="grid-3">
            <label>
              Fecha
              <input type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            </label>
            <label>
              Puntuación <span className="hint">(1–5)</span>
              <input
                type="number"
                min={1}
                max={5}
                value={rating}
                onChange={(e) => setRating(e.target.value)}
              />
            </label>
            <button type="submit" className="primary-btn">
              Registrar
            </button>
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
        </form>

        <h3>Registro de ascensos</h3>
        <div className="ticks-list">
          {route.ticks.length === 0 && (
            <p className="hint" style={{ fontSize: 13 }}>
              Todavía no hay ascensos registrados.
            </p>
          )}
          {route.ticks.map((t) => (
            <div key={t.id} className="tick-row">
              <div>
                <span className="tr-who">{t.climber}</span> · {t.date}
                {t.rating ? ` · ${'★'.repeat(t.rating)}` : ''}
                {t.notes && <div className="tr-notes">{t.notes}</div>}
              </div>
              <button className="tick-remove" title="Eliminar" onClick={() => onDeleteTick(t.id)}>
                &times;
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
