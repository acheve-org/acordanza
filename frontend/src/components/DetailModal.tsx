import { useState, type FormEvent } from 'react';
import type { CreateTickInput, RouteDetail } from '../api/types';

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
              {route.grade} ({route.gradeSystem}) · {route.crag} · {route.type}
            </p>
          </div>
          <div className="detail-actions">
            <button className="ghost-btn small" onClick={onEdit}>
              Edit
            </button>
            <button className="ghost-btn small danger" onClick={onDelete}>
              Delete
            </button>
          </div>
        </div>
        <p className="detail-desc">{route.description || 'No description yet.'}</p>
        {route.firstAscent && <p className="detail-fa">First ascent: {route.firstAscent}</p>}
        <div className="tag-row">
          {tags.map((t) => (
            <span key={t} className="tag-chip">
              {t}
            </span>
          ))}
        </div>

        <hr className="divider" />

        <h3>Log an ascent</h3>
        <form className="tick-form" onSubmit={handleTickSubmit}>
          <div className="grid-3">
            <label>
              Date
              <input type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            </label>
            <label>
              Rating <span className="hint">(1–5)</span>
              <input
                type="number"
                min={1}
                max={5}
                value={rating}
                onChange={(e) => setRating(e.target.value)}
              />
            </label>
            <button type="submit" className="primary-btn">
              Log it
            </button>
          </div>
          <label>
            Notes
            <input
              type="text"
              placeholder="Sent 2nd go, crux was the crimp move…"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </label>
        </form>

        <h3>Ascent log</h3>
        <div className="ticks-list">
          {route.ticks.length === 0 && (
            <p className="hint" style={{ fontSize: 13 }}>
              No ascents logged yet.
            </p>
          )}
          {route.ticks.map((t) => (
            <div key={t.id} className="tick-row">
              <div>
                <span className="tr-who">{t.climber}</span> · {t.date}
                {t.rating ? ` · ${'★'.repeat(t.rating)}` : ''}
                {t.notes && <div className="tr-notes">{t.notes}</div>}
              </div>
              <button className="tick-remove" title="Remove" onClick={() => onDeleteTick(t.id)}>
                &times;
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
