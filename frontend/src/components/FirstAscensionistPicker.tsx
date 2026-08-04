import { useEffect, useMemo, useState } from 'react';
import { Button, Chip, ModalActions } from 'acordanza-design-system';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import type { FirstAscensionistSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

interface Props {
  value: number[];
  onChange: (firstAscensionistIds: number[]) => void;
}

export function FirstAscensionistPicker({ value, onChange }: Props) {
  const { hasPermission } = useAuth();
  const canCreate = hasPermission(Permissions.firstAscensionistCreate);
  const [firstAscensionists, setFirstAscensionists] = useState<FirstAscensionistSummary[]>([]);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');

  useEffect(() => {
    firstAscensionistsApi.listFirstAscensionists().then(setFirstAscensionists);
  }, []);

  const byId = useMemo(() => new Map(firstAscensionists.map((o) => [o.id, o])), [firstAscensionists]);
  const selected = value.map((id) => byId.get(id)).filter((o): o is FirstAscensionistSummary => o !== undefined);

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    return firstAscensionists.filter((o) => !value.includes(o.id) && (!q || o.name.toLowerCase().includes(q)));
  }, [firstAscensionists, value, query]);

  function addFirstAscensionist(id: number) {
    onChange([...value, id]);
    setQuery('');
    setOpen(false);
  }

  function removeFirstAscensionist(id: number) {
    onChange(value.filter((v) => v !== id));
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await firstAscensionistsApi.createFirstAscensionist(name);
    setFirstAscensionists((prev) => [...prev, created]);
    setNewName('');
    setShowCreate(false);
    onChange([...value, created.id]);
  }

  return (
    <div className="combo">
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
      <input
        type="text"
        placeholder="Buscar aperturista…"
        value={query}
        onFocus={() => setOpen(true)}
        onClick={() => setOpen(true)}
        onChange={(e) => setQuery(e.target.value)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
      />
      {open && (
        <div className="combo-list">
          {matches.length === 0 && <div className="combo-empty hint">Sin coincidencias</div>}
          {matches.map((o) => (
            <button
              type="button"
              key={o.id}
              className="combo-option"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => addFirstAscensionist(o.id)}
            >
              {o.name}
              {o.linkedUserName ? ` · ${o.linkedUserName}` : ''}
            </button>
          ))}
        </div>
      )}
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
