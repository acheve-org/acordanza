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
        <h1>Routes ({routes.length})</h1>
      </div>
      <div className="routes-list">
        {routes.map((r) => (
          <RouteCard key={r.id} route={r} onClick={() => onSelect(r.id)} />
        ))}
      </div>
      {routes.length === 0 && (
        <p className="empty-state">No routes match yet. Add the first one, or widen your search.</p>
      )}
    </section>
  );
}
