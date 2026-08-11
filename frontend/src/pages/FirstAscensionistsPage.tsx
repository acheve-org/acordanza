import { useEffect, useState } from 'react';
import { Navigate } from 'react-router';
import { Button, ConfirmModal, EmptyState, List, ListRow } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import type { FirstAscensionistSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { errorMessage } from '../api/errorMessage';
import { useAuth } from '../context/AuthContext';
import { FirstAscensionistNameModal } from '../components/FirstAscensionistNameModal';

export function FirstAscensionistsPage() {
  const { hasPermission } = useAuth();
  // Gated on delete rather than edit: unlike first-ascensionist.edit (also granted
  // dynamically to any user linked to an FA record), first-ascensionist.delete is only
  // ever granted to moderators/admins — matching the "admins and moderators" scope in #109.
  const canManage = hasPermission(Permissions.firstAscensionistDelete);
  const canCreate = hasPermission(Permissions.firstAscensionistCreate);
  const canEdit = hasPermission(Permissions.firstAscensionistEdit);

  const [q, setQ] = useState('');
  const [items, setItems] = useState<FirstAscensionistSummary[]>([]);
  const [error, setError] = useState('');
  const [createModal, setCreateModal] = useState(false);
  const [editing, setEditing] = useState<FirstAscensionistSummary | null>(null);
  const [deleting, setDeleting] = useState<FirstAscensionistSummary | null>(null);

  function refresh() {
    firstAscensionistsApi
      .listFirstAscensionists(q || undefined, true)
      .then((result) => {
        setItems(result);
        setError('');
      })
      .catch((err) => setError(errorMessage(err, 'No se pudieron cargar los aperturistas')));
  }

  useEffect(() => {
    if (!canManage) return;
    let active = true;
    const handle = setTimeout(() => {
      firstAscensionistsApi
        .listFirstAscensionists(q || undefined, true)
        .then((result) => {
          if (!active) return;
          setItems(result);
          setError('');
        })
        .catch((err) => {
          if (!active) return;
          setError(errorMessage(err, 'No se pudieron cargar los aperturistas'));
        });
    }, 250);
    return () => {
      active = false;
      clearTimeout(handle);
    };
  }, [q, canManage]);

  if (!canManage) return <Navigate to="/" replace />;

  async function handleDelete(id: number) {
    try {
      await firstAscensionistsApi.deleteFirstAscensionist(id);
      refresh();
    } catch (err) {
      setError(errorMessage(err, 'No se pudo eliminar el aperturista'));
    }
  }

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

      {error && <p className="error-text">{error}</p>}

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
            refresh();
          }}
        />
      )}

      {editing && (
        <FirstAscensionistNameModal
          firstAscensionist={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            refresh();
          }}
        />
      )}

      {deleting && (
        <ConfirmModal
          message={`¿Eliminar al aperturista "${deleting.name}"?`}
          confirmLabel="Eliminar"
          danger
          onConfirm={() => handleDelete(deleting.id)}
          onClose={() => setDeleting(null)}
        />
      )}
    </>
  );
}
