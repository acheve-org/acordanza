import { NavLink, Navigate, Outlet } from 'react-router';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { usePendingTasks } from '../context/PendingTasksContext';

export function ManageLayout() {
  const { hasPermission } = useAuth();
  const { visible: pendingTasksVisible, total: pendingTasksTotal } = usePendingTasks();

  const canAreas = hasPermission(Permissions.areaEdit);
  const canUsers = hasPermission(Permissions.userManage);

  if (!canAreas && !canUsers && !pendingTasksVisible) return <Navigate to="/" replace />;

  return (
    <main className="side-nav-layout">
      <aside className="side-nav">
        <h2>Gestionar</h2>
        {canAreas && (
          <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/manage/areas">
            Sectores
          </NavLink>
        )}
        {canUsers && (
          <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/manage/users">
            Usuarios
          </NavLink>
        )}
        {pendingTasksVisible && (
          <NavLink
            className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
            to="/manage/pending-tasks"
          >
            <span>Tareas pendientes</span>
            {pendingTasksTotal > 0 && (
              <span className="side-nav-badge">{pendingTasksTotal > 99 ? '99+' : pendingTasksTotal}</span>
            )}
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

  if (hasPermission(Permissions.areaEdit)) return <Navigate to="/manage/areas" replace />;
  if (hasPermission(Permissions.userManage)) return <Navigate to="/manage/users" replace />;
  if (pendingTasksVisible) return <Navigate to="/manage/pending-tasks" replace />;
  return <Navigate to="/" replace />;
}
