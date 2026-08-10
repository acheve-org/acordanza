import { useId } from 'react';
import type { ReactNode } from 'react';
import { useDisclosure } from '../hooks/useDisclosure';

export function SideNav({ title, children }: { title: string; children: ReactNode }) {
  const { open, toggle, containerRef, triggerRef } = useDisclosure();
  const itemsId = useId();

  return (
    <aside className="side-nav" ref={containerRef}>
      <div className="side-nav-header">
        <h2>{title}</h2>
        <button
          type="button"
          className="nav-toggle"
          aria-label={`Secciones de ${title}`}
          aria-haspopup="true"
          aria-expanded={open}
          aria-controls={itemsId}
          ref={triggerRef}
          onClick={toggle}
        >
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z" />
          </svg>
        </button>
      </div>
      <div id={itemsId} className={`side-nav-items nav-popup${open ? ' is-open' : ''}`}>
        {children}
      </div>
    </aside>
  );
}
