import { useEffect, useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import * as areasApi from '../api/areas';
import type { AreaSummary } from '../api/types';
import { errorMessage } from '../api/errorMessage';

interface Props {
  // Areas the user already has a pending/approved request for — filtered out of the picker.
  excludedAreaIds: number[];
  onClose: () => void;
  onRequested: () => void;
}

export function RequestAreaExpertModal({ excludedAreaIds, onClose, onRequested }: Props) {
  const [areas, setAreas] = useState<AreaSummary[] | null>(null);
  const [areaId, setAreaId] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  // The area list is only needed to populate this picker, so it's fetched when the modal
  // opens rather than on the page behind it.
  useEffect(() => {
    let cancelled = false;
    areasApi
      .listAreas()
      .then((result) => {
        if (!cancelled) setAreas(result);
      })
      .catch((err) => {
        if (cancelled) return;
        setAreas([]);
        setError(errorMessage(err, 'No se pudieron cargar los sectores'));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const excluded = new Set(excludedAreaIds);
  const available = (areas ?? []).filter((a) => a.parentAreaId === null && !excluded.has(a.id));

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!areaId) return;
    setError('');
    setSubmitting(true);
    try {
      await areaExpertsApi.requestAreaExpert(Number(areaId));
      onRequested();
    } catch (err) {
      setError(errorMessage(err, 'No se pudo solicitar el acceso'));
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
          <select value={areaId} disabled={areas === null} onChange={(e) => setAreaId(e.target.value)}>
            <option value="">
              {areas === null
                ? 'Cargando sectores…'
                : available.length === 0
                  ? 'No quedan sectores disponibles'
                  : 'Selecciona un sector…'}
            </option>
            {available.map((a) => (
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
