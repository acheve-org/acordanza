import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { Chip, EmptyState } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import type { FirstAscensionistProfile } from '../api/types';

// How many of an aperturista's sectors fit on their card before it collapses into "+N".
const MAX_AREA_CHIPS = 3;

export function FirstAscensionistsDirectoryPage() {
  const [q, setQ] = useState('');
  const [firstAscensionists, setFirstAscensionists] = useState<FirstAscensionistProfile[]>([]);

  useEffect(() => {
    // `active` guards against an earlier query's response landing after a later one's and
    // leaving the list disagreeing with the search box — the debounce alone doesn't stop
    // two requests from being in flight at once.
    let active = true;
    const handle = setTimeout(() => {
      firstAscensionistsApi.listFirstAscensionistProfiles(q).then((result) => {
        if (active) setFirstAscensionists(result);
      });
    }, 250);
    return () => {
      active = false;
      clearTimeout(handle);
    };
  }, [q]);

  return (
    <main className="fa-page">
      <h1>Sigue</h1>
      <p className="hint">
        ¿Te gusta cómo alguien elige y abre las vías? Descubre a los aperturistas, esas personas visionarias que
        dedican tiempo y esfuerzo en abrirnos nuevos caminos.
      </p>

      <input type="text" placeholder="Nombre del aperturista…" value={q} onChange={(e) => setQ(e.target.value)} />

      {firstAscensionists.length === 0 ? (
        <EmptyState>No hay aperturistas que coincidan con tu búsqueda.</EmptyState>
      ) : (
        <div className="fa-grid">
          {firstAscensionists.map((fa) => (
            <Link key={fa.id} className="fa-card" to={`/first-ascensionists/${fa.id}`}>
              <span className="fa-card-name">{fa.name}</span>
              <span className="fa-card-count">
                {fa.routeCount === 1 ? '1 vía abierta' : `${fa.routeCount} vías abiertas`}
              </span>
              {fa.areaNames.length > 0 && (
                <span className="tag-row">
                  {fa.areaNames.slice(0, MAX_AREA_CHIPS).map((areaName) => (
                    <Chip key={areaName}>{areaName}</Chip>
                  ))}
                  {fa.areaNames.length > MAX_AREA_CHIPS && <Chip>+{fa.areaNames.length - MAX_AREA_CHIPS}</Chip>}
                </span>
              )}
            </Link>
          ))}
        </div>
      )}
    </main>
  );
}
