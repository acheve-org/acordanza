import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Combobox, ModalActions } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { areaPath as pathFor } from '../api/areas';
import { queryKeys } from '../api/queryKeys';
import type { AreaSummary } from '../api/types';
import { validationStatusLabels } from '../api/types';
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
  const queryClient = useQueryClient();
  const canCreate = allowCreate && hasPermission(Permissions.areaCreate);
  const [query, setQuery] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newParentId, setNewParentId] = useState('');

  // The dropdown refetches on open (`onOpen` below, via `refetch`), so the only reason to
  // fetch up front is to resolve a *preselected* area into its "Padre › Hijo" label. With
  // nothing selected (route filters, a new route) the list isn't fetched at all until the
  // picker is opened.
  const { data: areas = [], refetch } = useQuery({
    queryKey: queryKeys.areaList(refreshToken),
    queryFn: () => areasApi.listAreas(),
    enabled: value !== null,
  });

  const byId = useMemo(() => new Map(areas.map((a) => [a.id, a])), [areas]);
  const selected = value !== null ? byId.get(value) : undefined;

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return areas;
    return areas.filter((a) => pathFor(a, byId).toLowerCase().includes(q));
  }, [areas, byId, query]);

  // A caller only ever sees their own pending/rejected areas here (the list is
  // approved-or-mine, see AreasController), so this marks "why is this one flagged?"
  // rather than a moderation cue.
  function optionLabel(a: AreaSummary): string {
    const path = pathFor(a, byId);
    return a.validationStatus === 'approved' ? path : `${path} (${validationStatusLabels[a.validationStatus]})`;
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await areasApi.createArea(name, newParentId ? Number(newParentId) : null);
    queryClient.invalidateQueries({ queryKey: ['areas'] });
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
        onOpen={() => refetch()}
        onQueryChange={setQuery}
        options={matches.map((a) => ({ key: a.id, label: optionLabel(a) }))}
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
