import type { RouteFilters } from '../api/types';
import { gearLabels } from '../api/types';
import { CragPicker } from './CragPicker';

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
      <CragPicker
        value={filters.cragId ? Number(filters.cragId) : null}
        onChange={(id) => set('cragId', id === null ? '' : String(id))}
        allowClear
      />

      <label className="field-label">Equipamiento</label>
      <select value={filters.gear} onChange={(e) => set('gear', e.target.value)}>
        <option value="">Cualquiera</option>
        {Object.entries(gearLabels).map(([value, label]) => (
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

      <label className="checkbox-field">
        <input
          type="checkbox"
          checked={filters.hasTopo === 'true'}
          onChange={(e) => set('hasTopo', e.target.checked ? 'true' : '')}
        />
        Solo con topo
      </label>

      <button className="primary-btn full-width" style={{ marginTop: 24 }} onClick={onAddRoute}>
        + Añadir vía
      </button>
    </aside>
  );
}
