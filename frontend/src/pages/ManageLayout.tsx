import { NavLink, Navigate, Outlet } from 'react-router';
import { Badge } from 'acordanza-design-system';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { usePendingTasks } from '../context/PendingTasksContext';

export function ManageLayout() {
  const { hasPermission } = useAuth();
  const { visible: pendingTasksVisible, total: pendingTasksTotal } = usePendingTasks();

  const canUsers = hasPermission(Permissions.userManage);
  const canAreas = hasPermission(Permissions.areaEdit);
  // first-ascensionist.delete (unlike .edit) is only ever granted to moderators/admins,
  // never dynamically to a linked FA user — see FirstAscensionistsPage.
  const canFirstAscensionists = hasPermission(Permissions.firstAscensionistDelete);

  if (!canUsers && !canAreas && !canFirstAscensionists && !pendingTasksVisible) return <Navigate to="/" replace />;

  return (
    <main className="side-nav-layout">
      <aside className="side-nav">
        <h2>Gestionar</h2>
        {canUsers && (
          <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/manage/users">
            Usuarios
          </NavLink>
        )}
        {canAreas && (
          <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/manage/areas">
            Sectores
          </NavLink>
        )}
        {canFirstAscensionists && (
          <NavLink
            className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
            to="/manage/first-ascensionists"
          >
            Aperturistas
          </NavLink>
        )}
        {pendingTasksVisible && (
          <NavLink
            className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
            to="/manage/pending-tasks"
          >
            <span>Tareas pendientes</span>
            <Badge count={pendingTasksTotal} />
          </NavLink>
        )}
      </aside>
      <section className="side-nav-content">
        <Outlet />
      </section>
    </main>
  );
}

export function ManageIndexRedirect() {
  const { hasPermission } = useAuth();
  const { visible: pendingTasksVisible } = usePendingTasks();

  if (hasPermission(Permissions.userManage)) return <Navigate to="/manage/users" replace />;
  if (hasPermission(Permissions.areaEdit)) return <Navigate to="/manage/areas" replace />;
  if (hasPermission(Permissions.firstAscensionistDelete)) return <Navigate to="/manage/first-ascensionists" replace />;
  if (pendingTasksVisible) return <Navigate to="/manage/pending-tasks" replace />;
  return <Navigate to="/" replace />;
}
