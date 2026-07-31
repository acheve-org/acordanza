import { useEffect, useState } from 'react';
import { Link, Navigate } from 'react-router';
import * as usersApi from '../api/users';
import type { PagedUsers, UserFilters } from '../api/types';
import { roleLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';

const emptyFilters: UserFilters = { q: '', role: '', page: 1, sortBy: 'name', sortDir: 'asc' };

const emptyResult: PagedUsers = { items: [], totalCount: 0, page: 1, pageSize: 10 };

const columns: { key: UserFilters['sortBy']; label: string }[] = [
  { key: 'name', label: 'Nombre' },
  { key: 'email', label: 'Correo electrónico' },
  { key: 'role', label: 'Rol' },
];

export function UsersPage() {
  const { hasPermission } = useAuth();
  const [filters, setFilters] = useState<UserFilters>(emptyFilters);
  const [result, setResult] = useState<PagedUsers>(emptyResult);
  const [error, setError] = useState('');

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
    <main className="users-page">
      <h1>Gestionar usuarios</h1>

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
        <p className="empty-state">No se han encontrado usuarios</p>
      ) : (
        <div className="users-table-wrap">
          <table className="users-table">
            <thead>
              <tr>
                {columns.map((col) => (
                  <th key={col.key}>
                    <button type="button" className="sort-btn" onClick={() => toggleSort(col.key)}>
                      {col.label}
                      {filters.sortBy === col.key && (filters.sortDir === 'asc' ? ' ▲' : ' ▼')}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {result.items.map((u) => (
                <tr key={u.id}>
                  <td>{u.name}</td>
                  <td>{u.email}</td>
                  <td>{roleLabels[u.role] ?? u.role}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="pagination">
        <button
          type="button"
          className="ghost-btn small"
          disabled={filters.page <= 1}
          onClick={() => set('page', filters.page - 1)}
        >
          Anterior
        </button>
        <span className="pagination-status">
          Página {filters.page} de {totalPages}
        </span>
        <button
          type="button"
          className="ghost-btn small"
          disabled={filters.page >= totalPages}
          onClick={() => set('page', filters.page + 1)}
        >
          Siguiente
        </button>
      </div>

      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
  );
}
