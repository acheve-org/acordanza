import { Navigate, NavLink, Outlet } from 'react-router';
import { useAuth } from '../context/AuthContext';

export function MyAcordanzaLayout() {
  const { isFirstAscensionist } = useAuth();

  return (
    <main className="side-nav-layout">
      <aside className="side-nav">
        <h2>Mi acordanza</h2>
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-areas">
          Mis sectores
        </NavLink>
        <NavLink
          className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
          to="/my-acordanza/claim-first-ascensionist"
        >
          Vincular aperturista
        </NavLink>
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-routes">
          Mis vías
        </NavLink>
        <NavLink className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza/my-invites">
          Mis invitaciones
        </NavLink>
        {isFirstAscensionist && (
          <NavLink
            className={({ isActive }) => `side-nav-item${isActive ? ' active' : ''}`}
            to="/my-acordanza/my-openings"
          >
            Mis aperturas
          </NavLink>
        )}
      </aside>
      <section className="side-nav-content">
        <Outlet />
      </section>
    </main>
  );
}

export function MyAcordanzaIndexRedirect() {
  return <Navigate to="/my-acordanza/my-areas" replace />;
}
