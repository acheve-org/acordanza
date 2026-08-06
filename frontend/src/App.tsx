import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Navigate, Outlet, Route, Routes, useLocation } from 'react-router';
import { Badge } from 'acordanza-design-system';
import { useAuth } from './context/AuthContext';
import { usePendingTasks } from './context/PendingTasksContext';
import { Permissions } from './api/permissions';
import { LoginScreen } from './components/LoginScreen';
import { MainView } from './pages/MainView';
import { SettingsPage } from './pages/SettingsPage';
import { UsersPage } from './pages/UsersPage';
import { ResetPasswordScreen } from './pages/ResetPasswordScreen';
import { AcceptInviteScreen } from './pages/AcceptInviteScreen';
import { RouteFormPage } from './pages/RouteFormPage';
import { MapPage } from './pages/MapPage';
import { AreasPage } from './pages/AreasPage';
import { MyAreasPage } from './pages/MyAreasPage';
import { ClaimFirstAscensionistPage } from './pages/ClaimFirstAscensionistPage';
import { MyOpeningsPage } from './pages/MyOpeningsPage';
import { MyTicksPage } from './pages/MyTicksPage';
import { MyInvitesPage } from './pages/MyInvitesPage';
import { PendingTasksPage } from './pages/PendingTasksPage';
import { ManageLayout, ManageIndexRedirect } from './pages/ManageLayout';
import { MyAcordanzaLayout, MyAcordanzaIndexRedirect } from './pages/MyAcordanzaLayout';

function AppLayout() {
  const { name, hasPermission, logout } = useAuth();
  const { visible: pendingTasksVisible, total: pendingTasksTotal } = usePendingTasks();
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!userMenuOpen) return;
    function handleClickOutside(e: MouseEvent) {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [userMenuOpen]);

  return (
    <div className="app">
      <header className="topbar">
        <Link to="/" className="brand">
          <span className="brand-mark">&#9670;</span>
          <span className="brand-name">Acordanza</span>
        </Link>
        <nav className="main-nav">
          <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/" end>
            Rutas
          </NavLink>
          <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/map">
            Mapa
          </NavLink>
        </nav>
        <div className="topbar-right">
          {(hasPermission(Permissions.areaEdit) || hasPermission(Permissions.userManage)) && (
            <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/manage">
              Gestionar
            </NavLink>
          )}
          {pendingTasksVisible && (
            <Link
              to="/manage/pending-tasks"
              className="notif-bell"
              aria-label="Tareas pendientes"
              title="Tareas pendientes"
            >
              <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M12 22c1.1 0 2-.9 2-2h-4c0 1.1.89 2 2 2zm6-6v-5c0-3.07-1.63-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.64 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z" />
              </svg>
              <Badge count={pendingTasksTotal} className="notif-badge" />
            </Link>
          )}
          <div className="user-menu" ref={userMenuRef}>
            <button
              className="user-menu-trigger"
              aria-label={name ?? ''}
              title={name ?? ''}
              aria-haspopup="true"
              aria-expanded={userMenuOpen}
              onClick={() => setUserMenuOpen((open) => !open)}
            >
              <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <circle cx="12" cy="8" r="4" />
                <path d="M4 20c0-4.418 3.582-8 8-8s8 3.582 8 8v1H4v-1z" />
              </svg>
            </button>
            {userMenuOpen && (
              <div className="user-menu-popup">
                <span className="user-menu-name">{name}</span>
                <div className="user-menu-divider" />
                <Link className="user-menu-item" to="/my-acordanza" onClick={() => setUserMenuOpen(false)}>
                  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <circle cx="12" cy="8" r="4" />
                    <path d="M4 20c0-4.418 3.582-8 8-8s8 3.582 8 8v1H4v-1z" />
                  </svg>
                  Mi acordanza
                </Link>
                <Link className="user-menu-item" to="/settings" onClick={() => setUserMenuOpen(false)}>
                  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.5.5 0 0 0 .12-.61l-1.92-3.32a.5.5 0 0 0-.59-.22l-2.39.96a7.03 7.03 0 0 0-1.62-.94l-.36-2.54a.5.5 0 0 0-.5-.41h-3.84a.5.5 0 0 0-.5.41l-.36 2.54a7.03 7.03 0 0 0-1.62.94l-2.39-.96a.5.5 0 0 0-.59.22L2.74 8.87a.5.5 0 0 0 .12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.5.5 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54a.5.5 0 0 0 .5.41h3.84a.5.5 0 0 0 .5-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32a.5.5 0 0 0-.12-.61l-2.01-1.58zM12 15.6a3.6 3.6 0 1 1 0-7.2 3.6 3.6 0 0 1 0 7.2z" />
                  </svg>
                  Configuración
                </Link>
                <div className="user-menu-divider" />
                <button
                  type="button"
                  className="user-menu-item"
                  onClick={() => {
                    setUserMenuOpen(false);
                    logout();
                  }}
                >
                  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <path d="M17 7l-1.41 1.41L17.17 10H9v2h8.17l-1.58 1.59L17 15l4-4-4-4zM5 5h7V3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h7v-2H5V5z" />
                  </svg>
                  Cerrar sesión
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <Outlet />
    </div>
  );
}

function App() {
  const { username, loading } = useAuth();
  const location = useLocation();

  // Reachable while logged out — this is where the password-reset email links.
  if (location.pathname === '/reset-password') return <ResetPasswordScreen />;
  // Reachable while logged out — this is where the invite email links.
  if (location.pathname === '/accept-invite') return <AcceptInviteScreen />;

  if (loading) return null;
  if (!username) return <LoginScreen />;

  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<MainView />} />
        <Route path="routes/new" element={<RouteFormPage />} />
        <Route path="routes/:id/edit" element={<RouteFormPage />} />
        <Route path="map" element={<MapPage />} />
        {/* Pre-#77 top-level paths — kept as redirects so old bookmarks/links still land. */}
        <Route path="my-areas" element={<Navigate to="/my-acordanza/my-areas" replace />} />
        <Route
          path="claim-first-ascensionist"
          element={<Navigate to="/my-acordanza/claim-first-ascensionist" replace />}
        />
        <Route path="my-routes" element={<Navigate to="/my-acordanza/my-routes" replace />} />
        <Route path="my-openings" element={<Navigate to="/my-acordanza/my-openings" replace />} />
        <Route path="my-acordanza" element={<MyAcordanzaLayout />}>
          <Route index element={<MyAcordanzaIndexRedirect />} />
          <Route path="my-areas" element={<MyAreasPage />} />
          <Route path="claim-first-ascensionist" element={<ClaimFirstAscensionistPage />} />
          <Route path="my-routes" element={<MyTicksPage />} />
          <Route path="my-invites" element={<MyInvitesPage />} />
          <Route path="my-openings" element={<MyOpeningsPage />} />
        </Route>
        <Route path="settings" element={<SettingsPage />} />
        <Route path="manage" element={<ManageLayout />}>
          <Route index element={<ManageIndexRedirect />} />
          <Route path="areas" element={<AreasPage />} />
          <Route path="users" element={<UsersPage />} />
          <Route path="pending-tasks" element={<PendingTasksPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

export default App;
