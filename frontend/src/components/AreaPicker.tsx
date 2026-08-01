import { useEffect, useMemo, useState } from 'react';
import * as areasApi from '../api/areas';
import type { AreaSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

interface Props {
  value: number | null;
  onChange: (areaId: number | null) => void;
  allowCreate?: boolean;
  allowClear?: boolean;
}

function pathFor(area: AreaSummary, byId: Map<number, AreaSummary>): string {
  const parts: string[] = [];
  let current: AreaSummary | undefined = area;
  while (current) {
    parts.unshift(current.name);
    current = current.parentAreaId !== null ? byId.get(current.parentAreaId) : undefined;
  }
  return parts.join(' › ');
}

export function AreaPicker({ value, onChange, allowCreate = false, allowClear = false }: Props) {
  const { hasPermission } = useAuth();
  const canCreate = allowCreate && hasPermission(Permissions.areaCreate);
  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newParentId, setNewParentId] = useState('');

  useEffect(() => {
    areasApi.listAreas().then(setAreas);
  }, []);

  const byId = useMemo(() => new Map(areas.map((a) => [a.id, a])), [areas]);
  const selected = value !== null ? byId.get(value) : undefined;

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return areas;
    return areas.filter((a) => pathFor(a, byId).toLowerCase().includes(q));
  }, [areas, byId, query]);

  function select(id: number | null) {
    onChange(id);
    setOpen(false);
    setQuery('');
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await areasApi.createArea(name, newParentId ? Number(newParentId) : null);
    setAreas((prev) => [...prev, created]);
    setNewName('');
    setNewParentId('');
    setShowCreate(false);
    select(created.id);
  }

  return (
    <div className="combo">
      <input
        type="text"
        placeholder="Buscar sector…"
        value={open ? query : selected ? pathFor(selected, byId) : ''}
        onFocus={() => {
          setOpen(true);
          setQuery('');
        }}
        onClick={() => setOpen(true)}
        onChange={(e) => setQuery(e.target.value)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
      />
      {open && (
        <div className="combo-list">
          {allowClear && (
            <button type="button" className="combo-option" onMouseDown={(e) => e.preventDefault()} onClick={() => select(null)}>
              Cualquier sector
            </button>
          )}
          {matches.length === 0 && <div className="combo-empty hint">Sin coincidencias</div>}
          {matches.map((a) => (
            <button
              type="button"
              key={a.id}
              className="combo-option"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => select(a.id)}
            >
              {pathFor(a, byId)}
            </button>
          ))}
        </div>
      )}
      {canCreate &&
        (!showCreate ? (
          <button type="button" className="ghost-btn small" onClick={() => setShowCreate(true)}>
            + Añadir nuevo sector
          </button>
        ) : (
          <div className="combo-create-form">
            <input
              type="text"
              placeholder="Nombre del nuevo sector"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
            />
            <select value={newParentId} onChange={(e) => setNewParentId(e.target.value)}>
              <option value="">Sector principal (sin padre)</option>
              {areas.map((a) => (
                <option key={a.id} value={a.id}>
                  {pathFor(a, byId)}
                </option>
              ))}
            </select>
            <div className="modal-actions">
              <button type="button" className="ghost-btn small" onClick={() => setShowCreate(false)}>
                Cancelar
              </button>
              <button type="button" className="primary-btn" onClick={handleCreate}>
                Añadir
              </button>
            </div>
          </div>
        ))}
    </div>
  );
}
