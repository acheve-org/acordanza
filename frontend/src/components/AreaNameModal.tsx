import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { errorMessage } from '../api/errorMessage';

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
      setError(errorMessage(err, area ? 'No se pudo actualizar el sector' : 'No se pudo crear el sector'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal onClose={onClose}>
      <h2>{area ? 'Editar sector' : parentAreaId === null ? 'Nuevo sector principal' : 'Nuevo subsector'}</h2>
      <form onSubmit={handleSubmit}>
        <label>
          Nombre
          <input type="text" required autoFocus value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <p className="error-text">{error}</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? (area ? 'Guardando…' : 'Creando…') : area ? 'Guardar' : 'Crear'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
