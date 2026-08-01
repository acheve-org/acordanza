import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import type { FirstAscensionistLinkRequestSummary, FirstAscensionistSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { RejectReasonModal } from '../components/RejectReasonModal';

const statusLabels: Record<FirstAscensionistLinkRequestSummary['status'], string> = {
  pending: 'Pendiente de aprobación',
  approved: 'Aprobado',
  rejected: 'Rechazado',
};

export function ClaimFirstAscensionistPage() {
  const { hasPermission } = useAuth();
  const canApprove = hasPermission(Permissions.firstAscensionistLinkApprove);

  const [q, setQ] = useState('');
  const [firstAscensionists, setFirstAscensionists] = useState<FirstAscensionistSummary[]>([]);
  const [mine, setMine] = useState<FirstAscensionistLinkRequestSummary[]>([]);
  const [pending, setPending] = useState<FirstAscensionistLinkRequestSummary[]>([]);
  const [error, setError] = useState('');
  const [rejecting, setRejecting] = useState<FirstAscensionistLinkRequestSummary | null>(null);

  function refreshFirstAscensionists() {
    firstAscensionistsApi.listFirstAscensionists(q).then(setFirstAscensionists);
  }

  function refreshRequests() {
    firstAscensionistLinksApi
      .listMyFirstAscensionistLinks()
      .then(setMine)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'No se pudieron cargar tus solicitudes'));
    if (canApprove) {
      firstAscensionistLinksApi.listPendingFirstAscensionistLinks().then(setPending);
    }
  }

  // Approving/rejecting a request (elsewhere on this same page) links or frees up
  // a FirstAscensionist row, so the search list above needs refreshing too — not
  // just the request lists — or it can keep showing a stale "Solicitar vínculo"
  // button for an entry that just got linked.
  function refreshAll() {
    refreshFirstAscensionists();
    refreshRequests();
  }

  useEffect(refreshRequests, [canApprove]);

  useEffect(() => {
    const handle = setTimeout(refreshFirstAscensionists, 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q]);

  const activeRequestFirstAscensionistIds = new Set(
    mine.filter((r) => r.status !== 'rejected').map((r) => r.firstAscensionistId),
  );

  async function handleRequest(firstAscensionistId: number) {
    setError('');
    try {
      await firstAscensionistLinksApi.requestFirstAscensionistLink(firstAscensionistId);
      refreshAll();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo solicitar el vínculo');
    }
  }

  async function handleApprove(id: number) {
    await firstAscensionistLinksApi.approveFirstAscensionistLink(id);
    refreshAll();
  }

  async function handleReject(reason: string) {
    if (!rejecting) return;
    await firstAscensionistLinksApi.rejectFirstAscensionistLink(rejecting.id, reason);
    setRejecting(null);
    refreshRequests();
  }

  async function handleCancel(id: number) {
    try {
      await firstAscensionistLinksApi.cancelFirstAscensionistLink(id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo retirar la solicitud');
    }
    refreshRequests();
  }

  return (
    <main className="settings-page">
      <h1>Vincular aperturista</h1>

      <section className="settings-section">
        <h2>Aperturistas</h2>
        <input
          type="text"
          placeholder="Buscar aperturista…"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        {firstAscensionists.length === 0 ? (
          <p className="empty-state">No se han encontrado aperturistas</p>
        ) : (
          <ul className="passkey-list">
            {firstAscensionists.map((fa) => (
              <li key={fa.id} className="tick-row">
                <div>
                  <div className="tr-who">{fa.name}</div>
                  {fa.linkedUserName && <div className="tr-notes">Vinculado a {fa.linkedUserName}</div>}
                  {!fa.linkedUserName && activeRequestFirstAscensionistIds.has(fa.id) && (
                    <div className="tr-notes">Ya tienes una solicitud para este aperturista</div>
                  )}
                </div>
                {!fa.linkedUserName && !activeRequestFirstAscensionistIds.has(fa.id) && (
                  <button type="button" className="ghost-btn small" onClick={() => handleRequest(fa.id)}>
                    Solicitar vínculo
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
        {error && <p className="error-text">{error}</p>}
      </section>

      <section className="settings-section">
        <h2>Mis solicitudes</h2>
        {mine.length === 0 ? (
          <p className="empty-state">Todavía no has solicitado ningún vínculo</p>
        ) : (
          <ul className="passkey-list">
            {mine.map((r) => (
              <li key={r.id} className="tick-row">
                <div>
                  <div className="tr-who">{r.firstAscensionistName}</div>
                  <div className="tr-notes">
                    {statusLabels[r.status]}
                    {r.status !== 'pending' && r.decidedByName && ` · ${r.decidedByName}`}
                    {r.status === 'rejected' && r.rejectionReason && ` · Motivo: ${r.rejectionReason}`}
                  </div>
                </div>
                {r.status === 'pending' && (
                  <button
                    type="button"
                    className="tick-remove"
                    aria-label="Retirar solicitud"
                    onClick={() => handleCancel(r.id)}
                  >
                    &times;
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {canApprove && (
        <section className="settings-section">
          <h2>Solicitudes pendientes</h2>
          {pending.length === 0 ? (
            <p className="empty-state">No hay solicitudes pendientes</p>
          ) : (
            <ul className="passkey-list">
              {pending.map((r) => (
                <li key={r.id} className="tick-row">
                  <div>
                    <div className="tr-who">
                      {r.userName} · {r.firstAscensionistName}
                    </div>
                    <div className="tr-notes">Solicitado el {new Date(r.requestedAt).toLocaleDateString('es-ES')}</div>
                  </div>
                  <div className="detail-actions">
                    <button type="button" className="ghost-btn small" onClick={() => handleApprove(r.id)}>
                      Aprobar
                    </button>
                    <button type="button" className="ghost-btn small danger" onClick={() => setRejecting(r)}>
                      Rechazar
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {rejecting && (
        <RejectReasonModal
          title={`Rechazar solicitud de ${rejecting.userName}`}
          onClose={() => setRejecting(null)}
          onReject={handleReject}
        />
      )}

      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
  );
}
