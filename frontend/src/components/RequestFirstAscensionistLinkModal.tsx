import { useEffect, useState } from 'react';
import { Button, EmptyState, Modal } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import type { FirstAscensionistSummary } from '../api/types';
import { ApiError } from '../api/client';

interface Props {
  excludedFirstAscensionistIds: number[];
  onClose: () => void;
  onRequested: () => void;
}

export function RequestFirstAscensionistLinkModal({ excludedFirstAscensionistIds, onClose, onRequested }: Props) {
  const [q, setQ] = useState('');
  const [firstAscensionists, setFirstAscensionists] = useState<FirstAscensionistSummary[]>([]);
  const [error, setError] = useState('');
  const [requestingId, setRequestingId] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    const handle = setTimeout(() => {
      firstAscensionistsApi
        .listFirstAscensionists(q)
        .then((results) => {
          if (!cancelled) setFirstAscensionists(results);
        })
        .catch((err) => {
          if (!cancelled) setError(err instanceof ApiError ? err.message : 'No se pudieron cargar los aperturistas');
        });
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(handle);
    };
  }, [q]);

  const excluded = new Set(excludedFirstAscensionistIds);
  const available = firstAscensionists.filter((fa) => !fa.linkedUserName && !excluded.has(fa.id));

  async function handleRequest(id: number) {
    setError('');
    setRequestingId(id);
    try {
      await firstAscensionistLinksApi.requestFirstAscensionistLink(id);
      onRequested();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo solicitar el vínculo');
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
        <ul className="passkey-list">
          {available.map((fa) => (
            <li key={fa.id} className="tick-row">
              <div className="tr-who">{fa.name}</div>
              <Button
                variant="ghost"
                small
                type="button"
                disabled={requestingId !== null}
                onClick={() => handleRequest(fa.id)}
              >
                Solicitar
              </Button>
            </li>
          ))}
        </ul>
      )}
      {error && <p className="error-text">{error}</p>}
    </Modal>
  );
}
