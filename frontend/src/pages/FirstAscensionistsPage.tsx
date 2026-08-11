import { useState } from 'react';
import { Navigate } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, ConfirmModal, EmptyState, List, ListRow } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { queryKeys } from '../api/queryKeys';
import type { FirstAscensionistSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { errorMessage } from '../api/errorMessage';
import { useAuth } from '../context/AuthContext';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { FirstAscensionistNameModal } from '../components/FirstAscensionistNameModal';

export function FirstAscensionistsPage() {
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  // Gated on delete rather than edit: unlike first-ascensionist.edit (also granted
  // dynamically to any user linked to an FA record), first-ascensionist.delete is only
  // ever granted to moderators/admins — matching the "admins and moderators" scope in #109.
  const canManage = hasPermission(Permissions.firstAscensionistDelete);
  const canCreate = hasPermission(Permissions.firstAscensionistCreate);
  const canEdit = hasPermission(Permissions.firstAscensionistEdit);

  const [q, setQ] = useState('');
  const [createModal, setCreateModal] = useState(false);
  const [editing, setEditing] = useState<FirstAscensionistSummary | null>(null);
  const [deleting, setDeleting] = useState<FirstAscensionistSummary | null>(null);

  const debouncedQ = useDebouncedValue(q);

  const { data: items = [], error } = useQuery({
    queryKey: queryKeys.firstAscensionists(debouncedQ),
    queryFn: () => firstAscensionistsApi.listFirstAscensionists(debouncedQ || undefined, true),
    enabled: canManage,
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => firstAscensionistsApi.deleteFirstAscensionist(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] }),
  });

  if (!canManage) return <Navigate to="/" replace />;

  const deleteError = deleteMutation.isError
    ? errorMessage(deleteMutation.error, 'No se pudo eliminar el aperturista')
    : '';

  return (
    <>
      <div className="page-header">
        <h1>Aperturistas</h1>
        {canCreate && (
          <Button type="button" onClick={() => setCreateModal(true)}>
            Nuevo aperturista
          </Button>
        )}
      </div>

      <div className="users-toolbar">
        <input type="text" placeholder="Buscar por nombre…" value={q} onChange={(e) => setQ(e.target.value)} />
      </div>

      {error && <p className="error-text">{errorMessage(error, 'No se pudieron cargar los aperturistas')}</p>}
      {deleteError && <p className="error-text">{deleteError}</p>}

      {items.length === 0 ? (
        <EmptyState>No se han encontrado aperturistas</EmptyState>
      ) : (
        <List>
          {items.map((fa) => (
            <ListRow
              key={fa.id}
              title={fa.name}
              subtitle={fa.linkedUserName ? `Usuario vinculado: ${fa.linkedUserName}` : undefined}
              trailing={
                <>
                  {canEdit && (
                    <Button variant="ghost" small type="button" onClick={() => setEditing(fa)}>
                      Editar
                    </Button>
                  )}
                  <Button variant="ghost" small danger type="button" onClick={() => setDeleting(fa)}>
                    Eliminar
                  </Button>
                </>
              }
            />
          ))}
        </List>
      )}

      {createModal && (
        <FirstAscensionistNameModal
          onClose={() => setCreateModal(false)}
          onSaved={() => {
            setCreateModal(false);
            queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] });
          }}
        />
      )}

      {editing && (
        <FirstAscensionistNameModal
          firstAscensionist={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] });
          }}
        />
      )}

      {deleting && (
        <ConfirmModal
          message={`¿Eliminar al aperturista "${deleting.name}"?`}
          confirmLabel="Eliminar"
          danger
          onConfirm={() => deleteMutation.mutate(deleting.id)}
          onClose={() => setDeleting(null)}
        />
      )}
    </>
  );
}
