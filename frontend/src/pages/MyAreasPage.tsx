import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, EmptyState, List, ListRow, RemoveButton } from 'acordanza-design-system';
import * as areaExpertsApi from '../api/areaExperts';
import { queryKeys } from '../api/queryKeys';
import { reviewStatusLabels } from '../api/types';
import { errorMessage } from '../api/errorMessage';
import { RequestAreaExpertModal } from '../components/RequestAreaExpertModal';

export function MyAreasPage() {
  const queryClient = useQueryClient();
  const [requesting, setRequesting] = useState(false);

  const { data: mine = [], error } = useQuery({
    queryKey: queryKeys.myAreaExperts(),
    queryFn: () => areaExpertsApi.listMyAreaExperts(),
  });

  const withdrawMutation = useMutation({
    mutationFn: (id: number) => areaExpertsApi.cancelAreaExpert(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['area-experts'] }),
  });

  const activeRequestAreaIds = mine.filter((e) => e.status !== 'rejected').map((e) => e.areaId);

  function handleRequested() {
    setRequesting(false);
    queryClient.invalidateQueries({ queryKey: ['area-experts'] });
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

      {error && <p className="error-text">{errorMessage(error, 'No se pudieron cargar tus sectores')}</p>}
      {withdrawMutation.isError && (
        <p className="error-text">{errorMessage(withdrawMutation.error, 'No se pudo retirar la solicitud')}</p>
      )}

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
                  {reviewStatusLabels[e.status]}
                  {e.status !== 'pending' && e.decidedByName && ` · ${e.decidedByName}`}
                  {e.status === 'rejected' && e.rejectionReason && ` · Motivo: ${e.rejectionReason}`}
                </>
              }
              trailing={
                e.status === 'pending' && (
                  <RemoveButton label="Retirar solicitud" onClick={() => withdrawMutation.mutate(e.id)} />
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
