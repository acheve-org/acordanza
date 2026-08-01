import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Navigate, Outlet, Route, Routes, useLocation } from 'react-router';
import { useAuth } from './context/AuthContext';
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

function AppLayout() {
  const { name, hasPermission, logout } = useAuth();
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
          <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/mapa">
            Mapa
          </NavLink>
          {(hasPermission(Permissions.areaEdit) || hasPermission(Permissions.userManage)) && (
            <span className="main-nav-separator" aria-hidden="true" />
          )}
          {hasPermission(Permissions.areaEdit) && (
            <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/sectores">
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
                <Link className="ghost-btn small full-width" to="/mis-sectores" onClick={() => setUserMenuOpen(false)}>
                  Mis sectores
                </Link>
                <Link className="ghost-btn small full-width" to="/settings" onClick={() => setUserMenuOpen(false)}>
                  Configuración
                </Link>
                <button
                  className="ghost-btn small full-width"
                  onClick={() => {
                    setUserMenuOpen(false);
                    logout();
                  }}
                >
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
        <Route path="mapa" element={<MapPage />} />
        <Route path="sectores" element={<AreasPage />} />
        <Route path="mis-sectores" element={<MyAreasPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="users" element={<UsersPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

export default App;
