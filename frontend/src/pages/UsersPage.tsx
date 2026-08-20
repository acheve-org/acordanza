import { useState } from 'react';
import { Navigate } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, EmptyState, Icon, List, ListRow, Pagination } from 'acordanza-design-system';
import * as usersApi from '../api/users';
import { queryKeys } from '../api/queryKeys';
import type { PagedUsers, UserAccount, UserFilters } from '../api/types';
import { roleLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { errorMessage } from '../api/errorMessage';
import { useAuth } from '../hooks/useAuth';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { formatDate } from '../format';
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
    segments.push(`Invitado por ${u.invitedByName} el ${formatDate(u.invitedAt)}`);
  } else if (u.invitedAt) {
    segments.push(`Invitado el ${formatDate(u.invitedAt)}`);
  }
  if (u.confirmedAt) {
    segments.push(`Confirmado el ${formatDate(u.confirmedAt)}`);
  }
  return segments.length > 0 ? segments.join(' · ') : null;
}

export function UsersPage() {
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const canManage = hasPermission(Permissions.userManage);
  const [filters, setFilters] = useState<UserFilters>(emptyFilters);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteSuccess, setInviteSuccess] = useState(false);

  const debouncedFilters = useDebouncedValue(filters);

  const { data: result = emptyResult, error } = useQuery({
    queryKey: queryKeys.users(debouncedFilters),
    queryFn: () => usersApi.listUsers(debouncedFilters),
    enabled: canManage,
  });

  if (!canManage) return <Navigate to="/" replace />;

  function handleInvited() {
    setInviteOpen(false);
    setInviteSuccess(true);
    queryClient.invalidateQueries({ queryKey: ['users'] });
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

      {error && <p className="error-text">{errorMessage(error, 'No se pudieron cargar los usuarios')}</p>}

      {result.items.length === 0 ? (
        <EmptyState>No se han encontrado usuarios</EmptyState>
      ) : (
        <>
          <div className="users-sort-bar">
            <span className="users-sort-label">Ordenar por</span>
            {columns.map((col) => (
              <button key={col.key} type="button" className="sort-btn" onClick={() => toggleSort(col.key)}>
                <Icon name="sort" size="sm" /> {col.label}
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

      <Pagination
        page={filters.page}
        totalCount={result.totalCount}
        pageSize={result.pageSize}
        onPageChange={(page) => set('page', page)}
      />

      {inviteOpen && (
        <InviteUserModal
          onClose={() => setInviteOpen(false)}
          onInvited={handleInvited}
        />
      )}
    </>
  );
}
