import { useEffect, useState } from 'react';
import { Button } from 'acordanza-design-system';
import type { RouteFilters } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import * as routesApi from '../api/routes';
import { useAuth } from '../context/AuthContext';
import { AreaPicker } from './AreaPicker';

interface Props {
  filters: RouteFilters;
  onChange: (filters: RouteFilters) => void;
  onAddRoute: () => void;
}

export function Filters({ filters, onChange, onAddRoute }: Props) {
  const { hasPermission } = useAuth();
  // Grades are free text on the backend, so the options are whichever ones are in use.
  const [grades, setGrades] = useState<string[]>([]);

  useEffect(() => {
    routesApi.listGrades().then(setGrades);
  }, []);

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
      <AreaPicker
        value={filters.areaId ? Number(filters.areaId) : null}
        onChange={(id) => set('areaId', id === null ? '' : String(id))}
        allowClear
      />

      <label className="field-label">Grado</label>
      <select value={filters.grade} onChange={(e) => set('grade', e.target.value)}>
        <option value="">Cualquiera</option>
        {grades.map((grade) => (
          <option key={grade} value={grade}>
            {grade}
          </option>
        ))}
      </select>

      <label className="field-label">Orientación</label>
      <select value={filters.orientation} onChange={(e) => set('orientation', e.target.value)}>
        <option value="">Cualquiera</option>
        {Object.entries(orientationLabels).map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>

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

      {hasPermission(Permissions.routeCreate) && (
        <Button fullWidth style={{ marginTop: 24 }} onClick={onAddRoute}>
          + Añadir vía
        </Button>
      )}
    </aside>
  );
}
