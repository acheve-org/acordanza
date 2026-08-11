import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { useSubmit } from '../hooks/useSubmit';

interface Props {
  parentAreaId: number | null;
  area?: { id: number; name: string };
  onClose: () => void;
  onSaved: () => void;
}

export function AreaNameModal({ parentAreaId, area, onClose, onSaved }: Props) {
  const [name, setName] = useState(area?.name ?? '');
  const { error, submitting, submit } = useSubmit(
    area ? 'No se pudo actualizar el sector' : 'No se pudo crear el sector',
  );

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    await submit(
      () => (area ? areasApi.updateArea(area.id, trimmed, parentAreaId) : areasApi.createArea(trimmed, parentAreaId)),
      onSaved,
    );
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
