import { Navigate, NavLink, Outlet } from 'react-router';
import { useAuth } from '../hooks/useAuth';
import { SideNav } from '../components/SideNav';

export function MyAcordanzaLayout() {
  const { isFirstAscensionist } = useAuth();

  return (
    <main className="side-nav-layout">
      <SideNav title="Mi acordanza">
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-routes">
          Mis ascensiones
        </NavLink>
        {isFirstAscensionist && (
            <NavLink
                className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
                to="/my-acordanza/my-openings"
            >
              Mis aperturas
            </NavLink>
        )}
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-areas">
          Mis sectores
        </NavLink>
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-invites">
          Mis invitaciones
        </NavLink>
        <NavLink
          className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
          to="/my-acordanza/claim-first-ascensionist"
        >
          Vincular aperturista
        </NavLink>
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/contributions">
          Mis contribuciones
        </NavLink>
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-imports">
        Mis importaciones
        </NavLink>
      </SideNav>
      <section className="side-nav-content">
        <Outlet />
      </section>
    </main>
  );
}

export function MyAcordanzaIndexRedirect() {
  return <Navigate to="/my-acordanza/my-routes" replace />;
}
