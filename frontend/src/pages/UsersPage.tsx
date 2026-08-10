import { useEffect, useState } from 'react';
import { Navigate } from 'react-router';
import { Button, Chip, EmptyState, List, ListRow, Pagination } from 'acordanza-design-system';
import * as usersApi from '../api/users';
import type { PagedUsers, UserAccount, UserFilters } from '../api/types';
import { roleLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { InviteUserModal } from '../components/InviteUserModal';

const emptyFilters: UserFilters = { q: '', role: '', page: 1, sortBy: 'name', sortDir: 'asc' };

const emptyResult: PagedUsers = { items: [], totalCount: 0, page: 1, pageSize: 10 };

const columns: { key: UserFilters['sortBy']; label: string }[] = [
  { key: 'name', label: 'Nombre' },
  { key: 'email', label: 'Correo electrónico' },
  { key: 'role', label: 'Rol' },
];

function inviteMeta(u: UserAccount): string | null {
  const segments: string[] = [];
  if (u.invitedByName && u.invitedAt) {
    segments.push(`Invitado por ${u.invitedByName} el ${new Date(u.invitedAt).toLocaleDateString('es-ES')}`);
  } else if (u.invitedAt) {
    segments.push(`Invitado el ${new Date(u.invitedAt).toLocaleDateString('es-ES')}`);
  }
  if (u.confirmedAt) {
    segments.push(`Confirmado el ${new Date(u.confirmedAt).toLocaleDateString('es-ES')}`);
  }
  return segments.length > 0 ? segments.join(' · ') : null;
}

export function UsersPage() {
  const { hasPermission } = useAuth();
  const [filters, setFilters] = useState<UserFilters>(emptyFilters);
  const [result, setResult] = useState<PagedUsers>(emptyResult);
  const [error, setError] = useState('');
  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteSuccess, setInviteSuccess] = useState(false);

  function refreshUsers() {
    usersApi
      .listUsers(filters)
      .then((users) => {
        setResult(users);
        setError('');
      })
      .catch((err) => {
        setError(err instanceof ApiError ? err.message : 'No se pudieron cargar los usuarios');
      });
  }

  useEffect(() => {
    if (!hasPermission(Permissions.userManage)) return;
    let active = true;
    const handle = setTimeout(() => {
      usersApi
        .listUsers(filters)
        .then((users) => {
          if (!active) return;
          setResult(users);
          setError('');
        })
        .catch((err) => {
          if (!active) return;
          setError(err instanceof ApiError ? err.message : 'No se pudieron cargar los usuarios');
        });
    }, 250);
    return () => {
      active = false;
      clearTimeout(handle);
    };
  }, [filters, hasPermission]);

  if (!hasPermission(Permissions.userManage)) return <Navigate to="/" replace />;

  function handleInvited() {
    setInviteOpen(false);
    setInviteSuccess(true);
    refreshUsers();
  }

  function set<K extends keyof UserFilters>(key: K, value: UserFilters[K]) {
    setFilters((f) => ({ ...f, [key]: value, page: key === 'page' ? (value as number) : 1 }));
  }

  function toggleSort(key: UserFilters['sortBy']) {
    setFilters((f) => ({
      ...f,
      sortBy: key,
      sortDir: f.sortBy === key && f.sortDir === 'asc' ? 'desc' : 'asc',
      page: 1,
    }));
  }

  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));

  return (
    <>
      <div className="page-header">
        <h1>Usuarios</h1>
        <Button type="button" onClick={() => setInviteOpen(true)}>
          Invitar usuario
        </Button>
      </div>

      {inviteSuccess && <p className="success-text">Invitación enviada.</p>}

      <div className="users-toolbar">
        <input
          type="text"
          placeholder="Buscar por nombre, correo o rol…"
          value={filters.q}
          onChange={(e) => set('q', e.target.value)}
        />
        <select value={filters.role} onChange={(e) => set('role', e.target.value)}>
          <option value="">Todos los roles</option>
          {Object.entries(roleLabels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </div>

      {error && <p className="error-text">{error}</p>}

      {result.items.length === 0 ? (
        <EmptyState>No se han encontrado usuarios</EmptyState>
      ) : (
        <>
          <div className="users-sort-bar">
            <span className="users-sort-label">Ordenar por</span>
            {columns.map((col) => (
              <button key={col.key} type="button" className="sort-btn" onClick={() => toggleSort(col.key)}>
                {col.label}
                {filters.sortBy === col.key && (filters.sortDir === 'asc' ? ' ▲' : ' ▼')}
              </button>
            ))}
          </div>

          <List>
            {result.items.map((u) => {
              const meta = inviteMeta(u);
              return (
                <ListRow
                  key={u.id}
                  title={
                    <>
                      {u.name || '—'}
                      {u.pending && <Chip>Sin confirmar</Chip>}
                    </>
                  }
                  subtitle={
                    <>
                      {u.email}
                      {meta && <span className="user-row-meta">{meta}</span>}
                    </>
                  }
                  trailing={roleLabels[u.role] ?? u.role}
                />
              );
            })}
          </List>
        </>
      )}

      <Pagination page={filters.page} pageCount={totalPages} onPageChange={(page) => set('page', page)} />

      {inviteOpen && (
        <InviteUserModal
          onClose={() => setInviteOpen(false)}
          onInvited={handleInvited}
        />
      )}
    </>
  );
}
