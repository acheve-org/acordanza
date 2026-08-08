import { Link } from 'react-router';
import type { ReactNode } from 'react';
import { useAuth } from '../context/AuthContext';

interface Section {
  to: string;
  title: string;
  description: string;
  cta: string;
  icon: ReactNode;
}

// The app's four sections (issue #108) — the same four the top nav lists, in the same order.
const sections: Section[] = [
  {
    to: '/routes',
    title: 'Busca',
    description:
      'Encuentra las vías que quieres escalar. Busca por nombre, sector, grado, orientación o equipamiento.',
    cta: 'Buscar vías',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round">
        <circle cx="11" cy="11" r="6.5" />
        <path d="M16 16l4.5 4.5" />
      </svg>
    ),
  },
  {
    to: '/explore',
    title: 'Explora',
    description:
      'Encuentra zonas que no conoces en el mapa. Explora una zona y los sectores que la componen, y hazte una idea de lo que se puede escalar en ella.',
    cta: 'Ver el mapa',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 21s6.5-6.2 6.5-11A6.5 6.5 0 1 0 5.5 10c0 4.8 6.5 11 6.5 11z" />
        <circle cx="12" cy="10" r="2.25" />
      </svg>
    ),
  },
  {
    to: '/fa',
    title: 'Sigue',
    description:
      '¿Te gusta cómo alguien elige y abre las vías? Descubre a los aperturistas, esas personas visionarias que dedican tiempo y esfuerzo en abrirnos nuevos caminos.',
    cta: 'Ver aperturistas',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round">
        <path d="M3 20h18" />
        <path d="M7 20V4l9 2.5L7 9" />
      </svg>
    ),
  },
  {
    to: '/my-acordanza',
    title: 'Mi acordanza',
    description:
      'Consulta tus ascensiones, las personas a las que has invitado y los sectores de los que eres experto.',
    cta: 'Ir a lo mío',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="8.5" r="3.75" />
        <path d="M5 20c0-3.6 3.1-6 7-6s7 2.4 7 6" />
      </svg>
    ),
  },
];

export function HomePage() {
  const { name } = useAuth();

  return (
    <main className="home-page">
      <header className="home-header">
        <h1>{name ? `Hola, ${name}.` : 'Hola.'}</h1>
        <p className="home-lede">¿Qué quieres hacer hoy?</p>
      </header>

      <div className="home-grid">
        {sections.map((section) => (
          <Link key={section.to} className="home-card" to={section.to}>
            <span className="home-card-icon" aria-hidden="true">
              {section.icon}
            </span>
            <h2>{section.title}</h2>
            <p>{section.description}</p>
            <span className="home-card-cta">{section.cta} →</span>
          </Link>
        ))}
      </div>
    </main>
  );
}
