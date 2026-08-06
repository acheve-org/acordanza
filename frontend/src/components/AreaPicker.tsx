import { useEffect, useMemo, useState } from 'react';
import { Button, Combobox, ModalActions } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { areaPath as pathFor } from '../api/areas';
import type { AreaSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

interface Props {
  value: number | null;
  onChange: (areaId: number | null) => void;
  allowCreate?: boolean;
  allowClear?: boolean;
  // Bump this from a parent that mutates areas elsewhere (e.g. AreasPage's area tree) to
  // force a refetch — this component otherwise only refreshes on mount/focus.
  refreshToken?: number;
}

export function AreaPicker({ value, onChange, allowCreate = false, allowClear = false, refreshToken }: Props) {
  const { hasPermission } = useAuth();
  const canCreate = allowCreate && hasPermission(Permissions.areaCreate);
  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [query, setQuery] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newParentId, setNewParentId] = useState('');

  function refresh() {
    areasApi.listAreas().then(setAreas);
  }

  useEffect(refresh, [refreshToken]);

  const byId = useMemo(() => new Map(areas.map((a) => [a.id, a])), [areas]);
  const selected = value !== null ? byId.get(value) : undefined;

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return areas;
    return areas.filter((a) => pathFor(a, byId).toLowerCase().includes(q));
  }, [areas, byId, query]);

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await areasApi.createArea(name, newParentId ? Number(newParentId) : null);
    setAreas((prev) => [...prev, created]);
    setNewName('');
    setNewParentId('');
    setShowCreate(false);
    onChange(created.id);
  }

  return (
    <div className="picker-stack">
      <Combobox
        placeholder="Buscar sector…"
        closedValue={selected ? pathFor(selected, byId) : ''}
        onOpen={refresh}
        onQueryChange={setQuery}
        options={matches.map((a) => ({ key: a.id, label: pathFor(a, byId) }))}
        onSelect={onChange}
        leadingOption={allowClear ? { label: 'Cualquier sector', onSelect: () => onChange(null) } : undefined}
      />
      {canCreate &&
        (!showCreate ? (
          <Button variant="ghost" small type="button" onClick={() => setShowCreate(true)}>
            + Añadir nuevo sector
          </Button>
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
            <ModalActions>
              <Button variant="ghost" small type="button" onClick={() => setShowCreate(false)}>
                Cancelar
              </Button>
              <Button type="button" onClick={handleCreate}>
                Añadir
              </Button>
            </ModalActions>
          </div>
        ))}
    </div>
  );
}
