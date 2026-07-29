import { useEffect, useMemo, useState } from 'react';
import * as cragsApi from '../api/crags';
import type { CragSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

interface Props {
  value: number | null;
  onChange: (cragId: number | null) => void;
  allowCreate?: boolean;
  allowClear?: boolean;
}

function pathFor(crag: CragSummary, byId: Map<number, CragSummary>): string {
  const parts: string[] = [];
  let current: CragSummary | undefined = crag;
  while (current) {
    parts.unshift(current.name);
    current = current.parentCragId !== null ? byId.get(current.parentCragId) : undefined;
  }
  return parts.join(' › ');
}

export function CragPicker({ value, onChange, allowCreate = false, allowClear = false }: Props) {
  const { hasPermission } = useAuth();
  const canCreate = allowCreate && hasPermission(Permissions.cragCreate);
  const [crags, setCrags] = useState<CragSummary[]>([]);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newParentId, setNewParentId] = useState('');

  useEffect(() => {
    cragsApi.listCrags().then(setCrags);
  }, []);

  const byId = useMemo(() => new Map(crags.map((c) => [c.id, c])), [crags]);
  const selected = value !== null ? byId.get(value) : undefined;

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return crags;
    return crags.filter((c) => pathFor(c, byId).toLowerCase().includes(q));
  }, [crags, byId, query]);

  function select(id: number | null) {
    onChange(id);
    setOpen(false);
    setQuery('');
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await cragsApi.createCrag(name, newParentId ? Number(newParentId) : null);
    setCrags((prev) => [...prev, created]);
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
          {matches.map((c) => (
            <button
              type="button"
              key={c.id}
              className="combo-option"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => select(c.id)}
            >
              {pathFor(c, byId)}
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
              {crags.map((c) => (
                <option key={c.id} value={c.id}>
                  {pathFor(c, byId)}
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
