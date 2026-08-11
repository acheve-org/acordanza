import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, EmptyState, List, ListRow } from 'acordanza-design-system';
import * as usersApi from '../api/users';
import { queryKeys } from '../api/queryKeys';
import type { MyInvitesResponse } from '../api/types';
import { errorMessage } from '../api/errorMessage';
import { InviteUserModal } from '../components/InviteUserModal';

const emptyResult: MyInvitesResponse = { invites: [], invitesLeft: null, maxInvites: null };

export function MyInvitesPage() {
  const queryClient = useQueryClient();
  const [inviteOpen, setInviteOpen] = useState(false);

  const { data: result = emptyResult, error } = useQuery({
    queryKey: queryKeys.myInvites(),
    queryFn: () => usersApi.getMyInvites(),
  });

  function handleInvited() {
    setInviteOpen(false);
    queryClient.invalidateQueries({ queryKey: ['users'] });
  }

  return (
    <>
      <div className="page-header">
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

      {error && <p className="error-text">{errorMessage(error, 'No se pudieron cargar las invitaciones')}</p>}

      {result.invites.length === 0 ? (
        <EmptyState>Todavía no has invitado a nadie</EmptyState>
      ) : (
        <List>
          {result.invites.map((invite) => (
            <ListRow
              key={invite.email}
              title={invite.email}
              trailing={
                <>
                  <Chip>{invite.pending ? 'Pendiente' : 'Registrado'}</Chip>
                  {invite.hasInvitedOthers && <Chip>Ha invitado a otros</Chip>}
                </>
              }
            />
          ))}
        </List>
      )}

      {inviteOpen && (
        <InviteUserModal allowRoleSelection={false} onClose={() => setInviteOpen(false)} onInvited={handleInvited} />
      )}
    </>
  );
}
