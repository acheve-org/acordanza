import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, EmptyState, List, ListRow, Modal, Pagination } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import { queryKeys } from '../api/queryKeys';
import type { PagedFirstAscensionists } from '../api/types';
import { errorMessage } from '../api/errorMessage';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useSubmit } from '../hooks/useSubmit';

interface Props {
  onClose: () => void;
  onRequested: () => void;
}

const PAGE_SIZE = 10;

const emptyResult: PagedFirstAscensionists = { items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE };

export function RequestFirstAscensionistLinkModal({ onClose, onRequested }: Props) {
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const { error: requestError, submitting, submit } = useSubmit('No se pudo solicitar el vínculo');

  const debouncedQ = useDebouncedValue(q);

  const { data: result = emptyResult, error: fetchError } = useQuery({
    queryKey: queryKeys.firstAscensionistLinkCandidates(debouncedQ, page),
    queryFn: () =>
      firstAscensionistsApi.listFirstAscensionists({ q: debouncedQ, unlinkedOnly: true, page, pageSize: PAGE_SIZE }),
  });

  function handleQChange(value: string) {
    setQ(value);
    setPage(1);
  }

  const error = requestError || (fetchError ? errorMessage(fetchError, 'No se pudieron cargar los aperturistas') : '');

  async function handleRequest(id: number) {
    await submit(() => firstAscensionistLinksApi.requestFirstAscensionistLink(id), onRequested);
  }

  return (
    <Modal onClose={onClose}>
      <h2>Solicitar vínculo de aperturista</h2>
      <input
        type="text"
        placeholder="Buscar aperturista…"
        value={q}
        onChange={(e) => handleQChange(e.target.value)}
        autoFocus
      />
      {result.items.length === 0 ? (
        <EmptyState>No se han encontrado aperturistas disponibles</EmptyState>
      ) : (
        <>
          <List>
            {result.items.map((fa) => (
              <ListRow
                key={fa.id}
                title={fa.name}
                trailing={
                  <Button
                    variant="ghost"
                    small
                    type="button"
                    disabled={submitting}
                    onClick={() => handleRequest(fa.id)}
                  >
                    Solicitar
                  </Button>
                }
              />
            ))}
          </List>
          <Pagination page={page} totalCount={result.totalCount} pageSize={result.pageSize} onPageChange={setPage} />
        </>
      )}
      {error && <p className="error-text">{error}</p>}
    </Modal>
  );
}
