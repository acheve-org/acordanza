import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, EmptyState, List, ListRow, RemoveButton } from 'acordanza-design-system';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import { queryKeys } from '../api/queryKeys';
import { reviewStatusLabels } from '../api/types';
import { errorMessage } from '../api/errorMessage';
import { RequestFirstAscensionistLinkModal } from '../components/RequestFirstAscensionistLinkModal';

export function ClaimFirstAscensionistPage() {
  const queryClient = useQueryClient();
  const [requesting, setRequesting] = useState(false);

  const { data: mine = [], error } = useQuery({
    queryKey: queryKeys.myFirstAscensionistLinks(),
    queryFn: () => firstAscensionistLinksApi.listMyFirstAscensionistLinks(),
  });

  const cancelMutation = useMutation({
    mutationFn: (id: number) => firstAscensionistLinksApi.cancelFirstAscensionistLink(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['first-ascensionist-links'] }),
  });

  const activeClaim = mine.find((r) => r.status !== 'rejected');

  function handleRequested() {
    setRequesting(false);
    queryClient.invalidateQueries({ queryKey: ['first-ascensionist-links'] });
  }

  const intro =
    activeClaim === undefined
      ? 'Al vincularte con un aperturista podrás gestionar sus vías y aparecerás como tal en su ficha. Solo puedes vincularte con un aperturista.'
      : activeClaim.status === 'pending'
        ? `Tu solicitud para vincularte con ${activeClaim.firstAscensionistName} está pendiente de aprobación. Solo puedes vincularte con un aperturista, así que no puedes solicitar otro vínculo mientras tanto.`
        : `Estás vinculado con ${activeClaim.firstAscensionistName}. Solo puedes vincularte con un aperturista; si necesitas cambiarlo, pídeselo a un moderador.`;

  return (
    <>
      <div className="page-header">
        <h1>Vincular aperturista</h1>
        <Button type="button" onClick={() => setRequesting(true)} disabled={activeClaim !== undefined}>
          Solicitar vínculo
        </Button>
      </div>

      <p>{intro}</p>

      {error && <p className="error-text">{errorMessage(error, 'No se pudieron cargar tus solicitudes')}</p>}
      {cancelMutation.isError && (
        <p className="error-text">{errorMessage(cancelMutation.error, 'No se pudo retirar la solicitud')}</p>
      )}

      {mine.length === 0 ? (
        <EmptyState>Todavía no has solicitado ningún vínculo</EmptyState>
      ) : (
        <List>
          {mine.map((r) => (
            <ListRow
              key={r.id}
              title={r.firstAscensionistName}
              subtitle={
                <>
                  {reviewStatusLabels[r.status]}
                  {r.status !== 'pending' && r.decidedByName && ` · ${r.decidedByName}`}
                  {r.status === 'rejected' && r.rejectionReason && ` · Motivo: ${r.rejectionReason}`}
                </>
              }
              trailing={
                r.status === 'pending' && (
                  <RemoveButton label="Retirar solicitud" onClick={() => cancelMutation.mutate(r.id)} />
                )
              }
            />
          ))}
        </List>
      )}

      {requesting && (
        <RequestFirstAscensionistLinkModal onClose={() => setRequesting(false)} onRequested={handleRequested} />
      )}
    </>
  );
}
