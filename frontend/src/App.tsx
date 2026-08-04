import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Navigate, Outlet, Route, Routes, useLocation } from 'react-router';
import { Button } from 'acordanza-design-system';
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
import { PendingTasksPage } from './pages/PendingTasksPage';

function AppLayout() {
  const { name, hasPermission, isFirstAscensionist, logout } = useAuth();
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
          {(hasPermission(Permissions.areaEdit) || hasPermission(Permissions.userManage)) && (
            <span className="main-nav-separator" aria-hidden="true" />
          )}
          {hasPermission(Permissions.areaEdit) && (
            <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/areas">
              Sectores
            </NavLink>
          )}
          {hasPermission(Permissions.userManage) && (
            <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/users">
              Usuarios
            </NavLink>
          )}
        </nav>
        <div className="topbar-right">
          {pendingTasksVisible && (
            <Link to="/pending-tasks" className="notif-bell" aria-label="Tareas pendientes" title="Tareas pendientes">
              <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M12 22c1.1 0 2-.9 2-2h-4c0 1.1.89 2 2 2zm6-6v-5c0-3.07-1.63-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.64 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z" />
              </svg>
              {pendingTasksTotal > 0 && (
                <span className="notif-badge">{pendingTasksTotal > 99 ? '99+' : pendingTasksTotal}</span>
              )}
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
                <Button as={Link} variant="ghost" small fullWidth to="/my-areas" onClick={() => setUserMenuOpen(false)}>
                  Mis sectores
                </Button>
                <Button
                  as={Link}
                  variant="ghost"
                  small
                  fullWidth
                  to="/claim-first-ascensionist"
                  onClick={() => setUserMenuOpen(false)}
                >
                  Vincular aperturista
                </Button>
                <Button as={Link} variant="ghost" small fullWidth to="/my-routes" onClick={() => setUserMenuOpen(false)}>
                  Mis vías
                </Button>
                {isFirstAscensionist && (
                  <Button as={Link} variant="ghost" small fullWidth to="/my-openings" onClick={() => setUserMenuOpen(false)}>
                    Mis aperturas
                  </Button>
                )}
                <Button as={Link} variant="ghost" small fullWidth to="/settings" onClick={() => setUserMenuOpen(false)}>
                  Configuración
                </Button>
                <Button
                  variant="ghost"
                  small
                  fullWidth
                  onClick={() => {
                    setUserMenuOpen(false);
                    logout();
                  }}
                >
                  Cerrar sesión
                </Button>
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
        <Route path="areas" element={<AreasPage />} />
        <Route path="my-areas" element={<MyAreasPage />} />
        <Route path="claim-first-ascensionist" element={<ClaimFirstAscensionistPage />} />
        <Route path="my-routes" element={<MyTicksPage />} />
        <Route path="my-openings" element={<MyOpeningsPage />} />
        <Route path="pending-tasks" element={<PendingTasksPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="users" element={<UsersPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

export default App;
