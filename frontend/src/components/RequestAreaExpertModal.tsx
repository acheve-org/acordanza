import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import type { AreaSummary } from '../api/types';
import { ApiError } from '../api/client';

interface Props {
  areas: AreaSummary[];
  onClose: () => void;
  onRequested: () => void;
}

export function RequestAreaExpertModal({ areas, onClose, onRequested }: Props) {
  const [areaId, setAreaId] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!areaId) return;
    setError('');
    setSubmitting(true);
    try {
      await areaExpertsApi.requestAreaExpert(Number(areaId));
      onRequested();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo solicitar el acceso');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal onClose={onClose}>
      <h2>Solicitar acceso a un sector</h2>
      <form onSubmit={handleSubmit}>
        <label>
          Sector
          <select value={areaId} onChange={(e) => setAreaId(e.target.value)}>
            <option value="">Selecciona un sector…</option>
            {areas.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </label>
        <p className="error-text">{error}</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={!areaId || submitting}>
            {submitting ? 'Solicitando…' : 'Solicitar'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
