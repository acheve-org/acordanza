import { useEffect, useMemo, useState } from 'react';
import * as openersApi from '../api/routeOpeners';
import type { RouteOpenerSummary, UserSummary } from '../api/types';

interface Props {
  value: number[];
  onChange: (openerIds: number[]) => void;
}

export function OpenerPicker({ value, onChange }: Props) {
  const [openers, setOpeners] = useState<RouteOpenerSummary[]>([]);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newUserId, setNewUserId] = useState('');

  useEffect(() => {
    openersApi.listOpeners().then(setOpeners);
    openersApi.listLinkableUsers().then(setUsers);
  }, []);

  const byId = useMemo(() => new Map(openers.map((o) => [o.id, o])), [openers]);
  const selected = value.map((id) => byId.get(id)).filter((o): o is RouteOpenerSummary => o !== undefined);

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    return openers.filter((o) => !value.includes(o.id) && (!q || o.name.toLowerCase().includes(q)));
  }, [openers, value, query]);

  function addOpener(id: number) {
    onChange([...value, id]);
    setQuery('');
    setOpen(false);
  }

  function removeOpener(id: number) {
    onChange(value.filter((v) => v !== id));
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await openersApi.createOpener(name, newUserId || null);
    setOpeners((prev) => [...prev, created]);
    setNewName('');
    setNewUserId('');
    setShowCreate(false);
    onChange([...value, created.id]);
  }

  return (
    <div className="combo">
      {selected.length > 0 && (
        <div className="tag-row" style={{ marginBottom: 8 }}>
          {selected.map((o) => (
            <span key={o.id} className="tag-chip chip-removable">
              {o.name}
              {o.userName && <span className="hint"> · {o.userName}</span>}
              <button type="button" className="chip-remove" onClick={() => removeOpener(o.id)}>
                &times;
              </button>
            </span>
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
              onClick={() => addOpener(o.id)}
            >
              {o.name}
              {o.userName ? ` · ${o.userName}` : ''}
            </button>
          ))}
        </div>
      )}
      {!showCreate ? (
        <button type="button" className="ghost-btn small" onClick={() => setShowCreate(true)}>
          + Nuevo aperturista
        </button>
      ) : (
        <div className="combo-create-form">
          <input
            type="text"
            placeholder="Nombre del aperturista"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <select value={newUserId} onChange={(e) => setNewUserId(e.target.value)}>
            <option value="">Sin vincular a cuenta</option>
            {users.map((u) => (
              <option key={u.id} value={u.id}>
                {u.userName}
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
      )}
    </div>
  );
}
