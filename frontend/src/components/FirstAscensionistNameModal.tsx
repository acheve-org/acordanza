import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { ApiError } from '../api/client';

interface Props {
  firstAscensionist?: { id: number; name: string };
  onClose: () => void;
  onSaved: () => void;
}

export function FirstAscensionistNameModal({ firstAscensionist, onClose, onSaved }: Props) {
  const [name, setName] = useState(firstAscensionist?.name ?? '');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    setError('');
    setSubmitting(true);
    try {
      if (firstAscensionist) {
        await firstAscensionistsApi.updateFirstAscensionist(firstAscensionist.id, trimmed);
      } else {
        await firstAscensionistsApi.createFirstAscensionist(trimmed);
      }
      onSaved();
    } catch (err) {
      setError(
        err instanceof ApiError
          ? err.message
          : firstAscensionist
            ? 'No se pudo actualizar el aperturista'
            : 'No se pudo crear el aperturista',
      );
    } finally {
      setSubmitting(false);
    }
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
