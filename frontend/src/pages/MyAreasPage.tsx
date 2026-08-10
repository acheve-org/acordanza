import { useEffect, useState } from 'react';
import { Button, EmptyState, List, ListRow, RemoveButton } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import type { AreaExpert } from '../api/types';
import { ApiError } from '../api/client';
import { RequestAreaExpertModal } from '../components/RequestAreaExpertModal';

const statusLabels: Record<AreaExpert['status'], string> = {
  pending: 'Solicitud pendiente de aprobación',
  approved: 'Experto aprobado',
  rejected: 'Solicitud rechazada',
};

export function MyAreasPage() {
  const [mine, setMine] = useState<AreaExpert[]>([]);
  const [error, setError] = useState('');
  const [requesting, setRequesting] = useState(false);

  function refresh() {
    areaExpertsApi
      .listMyAreaExperts()
      .then(setMine)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'No se pudieron cargar tus sectores'));
  }

  useEffect(refresh, []);

  const activeRequestAreaIds = mine.filter((e) => e.status !== 'rejected').map((e) => e.areaId);

  async function handleWithdraw(id: number) {
    try {
      await areaExpertsApi.cancelAreaExpert(id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo retirar la solicitud');
    }
    refresh();
  }

  function handleRequested() {
    setRequesting(false);
    refresh();
  }

  return (
    <>
      <div className="page-header">
        <h1>Mis sectores</h1>
        <Button type="button" onClick={() => setRequesting(true)}>
          Solicitar acceso
        </Button>
      </div>

      <p>
        Como experto de un sector puedes validar las vías de ese sector y de sus subsectores desde la ficha de cada
        vía.
      </p>

      {error && <p className="error-text">{error}</p>}

      {mine.length === 0 ? (
        <EmptyState>Todavía no eres experto de ningún sector</EmptyState>
      ) : (
        <List>
          {mine.map((e) => (
            <ListRow
              key={e.id}
              title={e.areaName}
              subtitle={
                <>
                  {statusLabels[e.status]}
                  {e.status !== 'pending' && e.decidedByName && ` · ${e.decidedByName}`}
                  {e.status === 'rejected' && e.rejectionReason && ` · Motivo: ${e.rejectionReason}`}
                </>
              }
              trailing={
                e.status === 'pending' && (
                  <RemoveButton label="Retirar solicitud" onClick={() => handleWithdraw(e.id)} />
                )
              }
            />
          ))}
        </List>
      )}

      {requesting && (
        <RequestAreaExpertModal
          excludedAreaIds={activeRequestAreaIds}
          onClose={() => setRequesting(false)}
          onRequested={handleRequested}
        />
      )}
    </>
  );
}
