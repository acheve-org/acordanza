import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { useSubmit } from '../hooks/useSubmit';

interface Props {
  firstAscensionist?: { id: number; name: string };
  onClose: () => void;
  onSaved: () => void;
}

export function FirstAscensionistNameModal({ firstAscensionist, onClose, onSaved }: Props) {
  const [name, setName] = useState(firstAscensionist?.name ?? '');
  const { error, submitting, submit } = useSubmit(
    firstAscensionist ? 'No se pudo actualizar el aperturista' : 'No se pudo crear el aperturista',
  );

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    await submit(
      () =>
        firstAscensionist
          ? firstAscensionistsApi.updateFirstAscensionist(firstAscensionist.id, trimmed)
          : firstAscensionistsApi.createFirstAscensionist(trimmed),
      onSaved,
    );
  }

  return (
    <Modal onClose={onClose}>
      <h2>{firstAscensionist ? 'Editar aperturista' : 'Nuevo aperturista'}</h2>
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
            {submitting ? (firstAscensionist ? 'Guardando…' : 'Creando…') : firstAscensionist ? 'Guardar' : 'Crear'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
