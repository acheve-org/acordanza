import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, Combobox, ModalActions } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import { queryKeys } from '../api/queryKeys';
import type { FirstAscensionistSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

interface Props {
  value: number[];
  onChange: (firstAscensionistIds: number[]) => void;
}

export function FirstAscensionistPicker({ value, onChange }: Props) {
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const canCreate = hasPermission(Permissions.firstAscensionistCreate);
  const [query, setQuery] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');

  // Same rule as AreaPicker: the dropdown refetches on open (via `refetch`), so the up-front
  // fetch only happens when there's already a selection whose chips need names (editing a
  // route). On /routes/new nothing is fetched until the picker is opened.
  const { data: firstAscensionists = [], refetch } = useQuery({
    queryKey: queryKeys.firstAscensionistList(),
    queryFn: () => firstAscensionistsApi.listFirstAscensionists(),
    enabled: value.length > 0,
  });

  const byId = useMemo(() => new Map(firstAscensionists.map((o) => [o.id, o])), [firstAscensionists]);
  const selected = value.map((id) => byId.get(id)).filter((o): o is FirstAscensionistSummary => o !== undefined);

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    return firstAscensionists.filter((o) => !value.includes(o.id) && (!q || o.name.toLowerCase().includes(q)));
  }, [firstAscensionists, value, query]);

  function addFirstAscensionist(id: number) {
    onChange([...value, id]);
  }

  function removeFirstAscensionist(id: number) {
    onChange(value.filter((v) => v !== id));
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await firstAscensionistsApi.createFirstAscensionist(name);
    queryClient.invalidateQueries({ queryKey: ['first-ascensionists'] });
    setNewName('');
    setShowCreate(false);
    onChange([...value, created.id]);
  }

  return (
    <div className="picker-stack">
      {selected.length > 0 && (
        <div className="tag-row" style={{ marginBottom: 8 }}>
          {selected.map((o) => (
            <Chip key={o.id} onRemove={() => removeFirstAscensionist(o.id)}>
              {o.name}
              {o.linkedUserName && <span className="hint"> · {o.linkedUserName}</span>}
            </Chip>
          ))}
        </div>
      )}
      <Combobox
        placeholder="Buscar aperturista…"
        onOpen={() => refetch()}
        onQueryChange={setQuery}
        options={matches.map((o) => ({
          key: o.id,
          label: o.linkedUserName ? `${o.name} · ${o.linkedUserName}` : o.name,
        }))}
        onSelect={addFirstAscensionist}
      />
      {canCreate &&
        (!showCreate ? (
          <Button variant="ghost" small type="button" onClick={() => setShowCreate(true)}>
            + Nuevo aperturista
          </Button>
        ) : (
          <div className="combo-create-form">
            <input
              type="text"
              placeholder="Nombre del aperturista"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
            />
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
