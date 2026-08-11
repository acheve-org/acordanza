import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, EmptyState, List, ListRow, Modal } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import { useDebouncedValue } from '../hooks/useDebouncedValue';

interface Props {
  onClose: () => void;
  onRequested: () => void;
}

export function RequestFirstAscensionistLinkModal({ onClose, onRequested }: Props) {
  const [q, setQ] = useState('');
  const [requestError, setRequestError] = useState('');
  const [requestingId, setRequestingId] = useState<number | null>(null);

  const debouncedQ = useDebouncedValue(q);

  const { data: firstAscensionists = [], error: fetchError } = useQuery({
    queryKey: queryKeys.firstAscensionistLinkCandidates(debouncedQ),
    queryFn: () => firstAscensionistsApi.listFirstAscensionists(debouncedQ),
  });

  const available = firstAscensionists.filter((fa) => !fa.linkedUserName);

  const error = requestError || (fetchError ? errorMessage(fetchError, 'No se pudieron cargar los aperturistas') : '');

  async function handleRequest(id: number) {
    setRequestError('');
    setRequestingId(id);
    try {
      await firstAscensionistLinksApi.requestFirstAscensionistLink(id);
      onRequested();
    } catch (err) {
      setRequestError(errorMessage(err, 'No se pudo solicitar el vínculo'));
      setRequestingId(null);
    }
  }

  return (
    <Modal onClose={onClose}>
      <h2>Solicitar vínculo de aperturista</h2>
      <input
        type="text"
        placeholder="Buscar aperturista…"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        autoFocus
      />
      {available.length === 0 ? (
        <EmptyState>No se han encontrado aperturistas disponibles</EmptyState>
      ) : (
        <List>
          {available.map((fa) => (
            <ListRow
              key={fa.id}
              title={fa.name}
              trailing={
                <Button
                  variant="ghost"
                  small
                  type="button"
                  disabled={requestingId !== null}
                  onClick={() => handleRequest(fa.id)}
                >
                  Solicitar
                </Button>
              }
            />
          ))}
        </List>
      )}
      {error && <p className="error-text">{error}</p>}
    </Modal>
  );
}
