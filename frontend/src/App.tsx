import { useEffect } from 'react';
import { Link, NavLink, Navigate, Outlet, Route, Routes, useLocation } from 'react-router';
import { Badge, Icon } from 'acordanza-design-system';
import { useAuth } from './hooks/useAuth';
import * as analytics from './lib/analytics';
import { usePendingTasks } from './hooks/usePendingTasks';
import { useDisclosure } from './hooks/useDisclosure';
import { Permissions } from './api/permissions';
import type { BackgroundLocationState } from './api/backgroundLocation';
import { LoginScreen } from './components/LoginScreen';
import { Brand } from './components/Brand';
import { MainNav } from './components/MainNav';
import { RouteDetailOverlay } from './components/RouteDetailOverlay';
import { HomePage } from './pages/HomePage';
import { SearchPage } from './pages/SearchPage';
import { SettingsPage } from './pages/SettingsPage';
import { UsersPage } from './pages/UsersPage';
import { ResetPasswordScreen } from './pages/ResetPasswordScreen';
import { AcceptInviteScreen } from './pages/AcceptInviteScreen';
import { ConfirmEmailChangeScreen } from './pages/ConfirmEmailChangeScreen';
import { RouteFormPage } from './pages/RouteFormPage';
import { ExplorePage } from './pages/ExplorePage';
import { AreaPage } from './pages/AreaPage';
import { FirstAscensionistsDirectoryPage } from './pages/FirstAscensionistsDirectoryPage';
import { FirstAscensionistPage } from './pages/FirstAscensionistPage';
import { AreasPage } from './pages/AreasPage';
import { FirstAscensionistsPage } from './pages/FirstAscensionistsPage';
import { MyAreasPage } from './pages/MyAreasPage';
import { ClaimFirstAscensionistPage } from './pages/ClaimFirstAscensionistPage';
import { MyContributionsPage } from './pages/MyContributionsPage';
import { MyOpeningsPage } from './pages/MyOpeningsPage';
import { MyTicksPage } from './pages/MyTicksPage';
import { MyInvitesPage } from './pages/MyInvitesPage';
import { MyImportsPage } from './pages/MyImportsPage';
import { PendingTasksPage } from './pages/PendingTasksPage';
import { ManageLayout, ManageIndexRedirect } from './pages/ManageLayout';
import { MyAcordanzaLayout, MyAcordanzaIndexRedirect } from './pages/MyAcordanzaLayout';

function AppLayout() {
  const { name, hasPermission, logout } = useAuth();
  const { visible: pendingTasksVisible, total: pendingTasksTotal } = usePendingTasks();
  const {
    open: userMenuOpen,
    toggle: toggleUserMenu,
    close: closeUserMenu,
    containerRef: userMenuRef,
    triggerRef: userMenuTriggerRef,
  } = useDisclosure<HTMLDivElement>();

  return (
    <div className="app">
      <header className="topbar">
        <Link to="/" className="brand">
          <Brand />
        </Link>
        <MainNav />
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
              <Icon name="bell" />
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
              ref={userMenuTriggerRef}
              onClick={toggleUserMenu}
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
                <Link className="user-menu-item" to="/settings" onClick={closeUserMenu}>
                  <Icon name="settings" />
                  Configuración
                </Link>
                <div className="user-menu-divider" />
                <button
                  type="button"
                  className="user-menu-item"
                  onClick={() => {
                    closeUserMenu();
                    logout();
                  }}
                >
                  <Icon name="logout" />
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

  useEffect(() => {
    analytics.trackPageView(location.pathname);
  }, [location.pathname]);

  // Reachable while logged out — this is where the password-reset email links.
  if (location.pathname === '/reset-password') return <ResetPasswordScreen />;
  // Reachable while logged out — this is where the invite email links.
  if (location.pathname === '/accept-invite') return <AcceptInviteScreen />;
  // Reachable regardless of auth state — the confirmation link opens in
  // whatever browser reads the new inbox, logged in or not.
  if (location.pathname === '/confirm-email-change') return <ConfirmEmailChangeScreen />;

  if (loading) return null;
  if (!username) return <LoginScreen />;

  // The "background location" pattern (plan 21): a route detail URL
  // (/routes/{area-slug-path}/{route-slug}) carries the page it was opened from in
  // router state. The main <Routes> below renders against that background location
  // instead of the real one, so the page behind the overlay stays mounted — its
  // filters/scroll/state untouched — while a second, small <Routes> (matching the
  // real location) renders RouteDetailOverlay on top. Absent that state (a pasted
  // link, a reload), the main <Routes> just renders the real location, and
  // SearchPage's own fresh-visit fallback (see SearchPage.tsx) synthesizes the state.
  const backgroundLocation = (location.state as BackgroundLocationState | null)?.backgroundLocation;

  return (
    <>
      <Routes location={backgroundLocation ?? location}>
        <Route element={<AppLayout />}>
          <Route index element={<HomePage />} />
          <Route path="routes" element={<SearchPage />} />
          <Route path="routes/new" element={<RouteFormPage />} />
          <Route path="routes/:id/edit" element={<RouteFormPage />} />
          {/* Fresh-visit fallback for a route detail URL with no background-location
              state (a pasted link, a reload) — static/dynamic segments above always
              rank above this splat, so routes/new and routes/:id/edit still match
              first; see SearchPage's own effect for how it opens the overlay. */}
          <Route path="routes/*" element={<SearchPage />} />
          <Route path="explore" element={<ExplorePage />} />
          <Route path="areas/*" element={<AreaPage />} />
          {/* "Sigue" — browsing aperturistas. The admin CRUD over the same records is a
              separate page under /manage/first-ascensionists. */}
          <Route path="fa" element={<FirstAscensionistsDirectoryPage />} />
          <Route path="fa/:slug" element={<FirstAscensionistPage />} />
          {/* Pre-slug: /first-ascensionists(/:id). An old numeric link can only land on the
              directory, not the specific opener, since an id can't be mapped to a slug
              without a fetch. */}
          <Route path="first-ascensionists/*" element={<Navigate to="/fa" replace />} />
          {/* Pre-#108: the routes list was the index page and the map lived at /map. */}
          <Route path="map" element={<Navigate to="/explore" replace />} />
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
            <Route path="contributions" element={<MyContributionsPage />} />
            <Route path="my-routes" element={<MyTicksPage />} />
            <Route path="my-invites" element={<MyInvitesPage />} />
            <Route path="my-imports" element={<MyImportsPage />} />
            <Route path="my-openings" element={<MyOpeningsPage />} />
          </Route>
          <Route path="settings" element={<SettingsPage />} />
          <Route path="manage" element={<ManageLayout />}>
            <Route index element={<ManageIndexRedirect />} />
            <Route path="areas" element={<AreasPage />} />
            <Route path="first-ascensionists" element={<FirstAscensionistsPage />} />
            <Route path="users" element={<UsersPage />} />
            <Route path="pending-tasks" element={<PendingTasksPage />} />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>

      {/* The overlay's own <Routes>, matched against the real (not background)
          location — only mounted once a backgroundLocation exists, so a fresh visit
          never renders it before SearchPage's fallback effect has synthesized one. */}
      {backgroundLocation && (
        <Routes>
          <Route path="routes/*" element={<RouteDetailOverlay />} />
        </Routes>
      )}
    </>
  );
}

export default App;
