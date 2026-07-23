import type { RouteFilters } from '../api/types';
import { routeTypeLabels } from '../api/types';

interface Props {
  filters: RouteFilters;
  onChange: (filters: RouteFilters) => void;
  onAddRoute: () => void;
}

export function Filters({ filters, onChange, onAddRoute }: Props) {
  function set<K extends keyof RouteFilters>(key: K, value: RouteFilters[K]) {
    onChange({ ...filters, [key]: value });
  }

  return (
    <aside className="filters">
      <h2>Buscar</h2>
      <input
        type="text"
        placeholder="Nombre de la vía o descripción…"
        value={filters.q}
        onChange={(e) => set('q', e.target.value)}
      />

      <label className="field-label">Sector</label>
      <input
        type="text"
        placeholder="Cualquier sector"
        value={filters.crag}
        onChange={(e) => set('crag', e.target.value)}
      />

      <label className="field-label">Tipo</label>
      <select value={filters.type} onChange={(e) => set('type', e.target.value)}>
        <option value="">Cualquiera</option>
        {Object.entries(routeTypeLabels).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>

      <label className="field-label">Etiqueta</label>
      <input
        type="text"
        placeholder="p. ej. regletas, clásica"
        value={filters.tag}
        onChange={(e) => set('tag', e.target.value)}
      />

      <button className="primary-btn full-width" style={{ marginTop: 24 }} onClick={onAddRoute}>
        + Añadir vía
      </button>
    </aside>
  );
}
