import type { RouteSummary } from '../api/types';
import { RouteCard } from './RouteCard';

interface Props {
  routes: RouteSummary[];
  onSelect: (id: number) => void;
}

export function RoutesList({ routes, onSelect }: Props) {
  return (
    <section className="routes-panel">
      <div className="routes-header">
        <h1>Vías ({routes.length})</h1>
      </div>
      <div className="routes-list">
        {routes.map((r) => (
          <RouteCard key={r.id} route={r} onClick={() => onSelect(r.id)} />
        ))}
      </div>
      {routes.length === 0 && (
        <p className="empty-state">No hay vías que coincidan. Añade la primera o amplía tu búsqueda.</p>
      )}
    </section>
  );
}
