import { useState, type FormEvent } from 'react';
import * as areasApi from '../api/areas';
import { ApiError } from '../api/client';

interface Props {
  parentAreaId: number | null;
  area?: { id: number; name: string };
  onClose: () => void;
  onSaved: () => void;
}

export function AreaNameModal({ parentAreaId, area, onClose, onSaved }: Props) {
  const [name, setName] = useState(area?.name ?? '');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    setError('');
    setSubmitting(true);
    try {
      if (area) {
        await areasApi.updateArea(area.id, trimmed, parentAreaId);
      } else {
        await areasApi.createArea(trimmed, parentAreaId);
      }
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : area ? 'No se pudo actualizar el sector' : 'No se pudo crear el sector');
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
        <h2>{area ? 'Editar sector' : parentAreaId === null ? 'Nuevo sector principal' : 'Nuevo subsector'}</h2>
        <form onSubmit={handleSubmit}>
          <label>
            Nombre
            <input type="text" required autoFocus value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <p className="error-text">{error}</p>
          <div className="modal-actions">
            <button type="button" className="ghost-btn" onClick={onClose}>
              Cancelar
            </button>
            <button type="submit" className="primary-btn" disabled={submitting}>
              {submitting ? (area ? 'Guardando…' : 'Creando…') : area ? 'Guardar' : 'Crear'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
