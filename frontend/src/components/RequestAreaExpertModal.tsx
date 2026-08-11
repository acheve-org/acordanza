import { useState, type FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import * as areasApi from '../api/areas';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';

interface Props {
  // Areas the user already has a pending/approved request for — filtered out of the picker.
  excludedAreaIds: number[];
  onClose: () => void;
  onRequested: () => void;
}

export function RequestAreaExpertModal({ excludedAreaIds, onClose, onRequested }: Props) {
  const [areaId, setAreaId] = useState('');
  const [submitError, setSubmitError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  // The area list is only needed to populate this picker, so it's fetched when the modal
  // opens rather than on the page behind it.
  const { data: areas, isLoading, error: fetchError } = useQuery({
    queryKey: queryKeys.areaList(),
    queryFn: () => areasApi.listAreas(),
  });

  const excluded = new Set(excludedAreaIds);
  const available = (areas ?? []).filter((a) => a.parentAreaId === null && !excluded.has(a.id));

  const error = submitError || (fetchError ? errorMessage(fetchError, 'No se pudieron cargar los sectores') : '');

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!areaId) return;
    setSubmitError('');
    setSubmitting(true);
    try {
      await areaExpertsApi.requestAreaExpert(Number(areaId));
      onRequested();
    } catch (err) {
      setSubmitError(errorMessage(err, 'No se pudo solicitar el acceso'));
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
          <select value={areaId} disabled={isLoading} onChange={(e) => setAreaId(e.target.value)}>
            <option value="">
              {isLoading
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
