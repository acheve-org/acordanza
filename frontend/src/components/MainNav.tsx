import { NavLink } from 'react-router';
import { Icon } from 'acordanza-design-system';
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
        <Icon name="menu" />
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
