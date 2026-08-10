import { NavLink } from 'react-router';
import { useDisclosure } from '../hooks/useDisclosure';

export function MainNav() {
  const { open, toggle, containerRef, triggerRef } = useDisclosure();

  return (
    <nav className="main-nav" ref={containerRef}>
      <button
        type="button"
        className="nav-toggle"
        aria-label="Menú"
        aria-haspopup="true"
        aria-expanded={open}
        aria-controls="main-nav-items"
        ref={triggerRef}
        onClick={toggle}
      >
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z" />
        </svg>
      </button>
      <div id="main-nav-items" className={`main-nav-items nav-popup${open ? ' is-open' : ''}`}>
        <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/routes">
          Busca
        </NavLink>
        <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/explore">
          Explora
        </NavLink>
        <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/fa">
          Sigue
        </NavLink>
        <NavLink className={({ isActive }) => `main-nav-item${isActive ? ' active' : ''}`} to="/my-acordanza">
          Mi acordanza
        </NavLink>
      </div>
    </nav>
  );
}
