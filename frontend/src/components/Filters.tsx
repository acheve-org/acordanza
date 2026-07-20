import type { RouteFilters } from '../api/types';

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
      <h2>Search</h2>
      <input
        type="text"
        placeholder="Route name or description…"
        value={filters.q}
        onChange={(e) => set('q', e.target.value)}
      />

      <label className="field-label">Crag</label>
      <input
        type="text"
        placeholder="Any crag"
        value={filters.crag}
        onChange={(e) => set('crag', e.target.value)}
      />

      <label className="field-label">Type</label>
      <select value={filters.type} onChange={(e) => set('type', e.target.value)}>
        <option value="">Any</option>
        <option value="sport">Sport</option>
        <option value="trad">Trad</option>
        <option value="boulder">Boulder</option>
        <option value="toprope">Top rope</option>
        <option value="alpine">Alpine</option>
      </select>

      <label className="field-label">Tag</label>
      <input
        type="text"
        placeholder="e.g. crimpy, classic"
        value={filters.tag}
        onChange={(e) => set('tag', e.target.value)}
      />

      <button className="primary-btn full-width" style={{ marginTop: 24 }} onClick={onAddRoute}>
        + Add route
      </button>
    </aside>
  );
}
