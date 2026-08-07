import { useEffect, useState } from 'react';
import { Button, Chip, EmptyState } from 'acordanza-design-system';
import * as usersApi from '../api/users';
import type { MyInvitesResponse } from '../api/types';
import { ApiError } from '../api/client';
import { InviteUserModal } from '../components/InviteUserModal';

const emptyResult: MyInvitesResponse = { invites: [], invitesLeft: null, maxInvites: null };

export function MyInvitesPage() {
  const [result, setResult] = useState<MyInvitesResponse>(emptyResult);
  const [error, setError] = useState('');
  const [inviteOpen, setInviteOpen] = useState(false);

  function refreshInvites() {
    usersApi
      .getMyInvites()
      .then((invites) => {
        setResult(invites);
        setError('');
      })
      .catch((err) => {
        setError(err instanceof ApiError ? err.message : 'No se pudieron cargar las invitaciones');
      });
  }

  useEffect(refreshInvites, []);

  function handleInvited() {
    setInviteOpen(false);
    refreshInvites();
  }

  return (
    <>
      <div className="users-page-header">
        <h1>Mis invitaciones</h1>
        <Button type="button" onClick={() => setInviteOpen(true)} disabled={result.invitesLeft === 0}>
          Invitar usuario
        </Button>
      </div>

      <p>
        {result.invitesLeft === null
          ? 'Invitaciones ilimitadas.'
          : `Te quedan ${result.invitesLeft} de ${result.maxInvites} invitaciones.`}
      </p>

      {error && <p className="error-text">{error}</p>}

      {result.invites.length === 0 ? (
        <EmptyState>Todavía no has invitado a nadie</EmptyState>
      ) : (
        <div className="users-table-wrap">
          <table className="users-table">
            <thead>
              <tr>
                <th>Correo electrónico</th>
                <th>Estado</th>
                <th>Ha invitado a otros</th>
              </tr>
            </thead>
            <tbody>
              {result.invites.map((invite) => (
                <tr key={invite.email}>
                  <td>{invite.email}</td>
                  <td>
                    <Chip>{invite.pending ? 'Pendiente' : 'Registrado'}</Chip>
                  </td>
                  <td>{invite.hasInvitedOthers ? <Chip>Sí</Chip> : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {inviteOpen && (
        <InviteUserModal allowRoleSelection={false} onClose={() => setInviteOpen(false)} onInvited={handleInvited} />
      )}
    </>
  );
}
