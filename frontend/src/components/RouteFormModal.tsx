import { useEffect, useState, type FormEvent } from 'react';
import type { RouteDetail, RouteSummary, UpsertRouteInput } from '../api/types';
import { routeTypeLabels } from '../api/types';

const emptyForm: UpsertRouteInput = {
  name: '',
  crag: '',
  grade: '',
  gradeSystem: 'YDS',
  type: 'sport',
  description: '',
  tags: '',
  firstAscent: '',
};

interface Props {
  open: boolean;
  editing: RouteSummary | RouteDetail | null;
  onCancel: () => void;
  onSave: (input: UpsertRouteInput) => Promise<void>;
}

export function RouteFormModal({ open, editing, onCancel, onSave }: Props) {
  const [form, setForm] = useState<UpsertRouteInput>(emptyForm);

  useEffect(() => {
    if (!open) return;
    setForm(
      editing
        ? {
            name: editing.name,
            crag: editing.crag,
            grade: editing.grade,
            gradeSystem: editing.gradeSystem,
            type: editing.type,
            description: editing.description,
            tags: editing.tags,
            firstAscent: editing.firstAscent,
          }
        : emptyForm,
    );
  }, [open, editing]);

  if (!open) return null;

  function set<K extends keyof UpsertRouteInput>(key: K, value: UpsertRouteInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    await onSave({
      ...form,
      name: form.name.trim(),
      crag: form.crag.trim(),
      grade: form.grade.trim(),
      description: form.description.trim(),
      tags: form.tags.trim(),
      firstAscent: form.firstAscent.trim(),
    });
  }

  return (
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onCancel();
      }}
    >
      <div className="modal">
        <h2>{editing ? 'Editar vía' : 'Añadir una vía'}</h2>
        <form onSubmit={handleSubmit}>
          <div className="grid-2">
            <label>
              Nombre
              <input type="text" required value={form.name} onChange={(e) => set('name', e.target.value)} />
            </label>
            <label>
              Sector / Zona
              <input type="text" required value={form.crag} onChange={(e) => set('crag', e.target.value)} />
            </label>
          </div>
          <div className="grid-3">
            <label>
              Grado
              <input
                type="text"
                required
                placeholder="5.10a, V4, E2 5b…"
                value={form.grade}
                onChange={(e) => set('grade', e.target.value)}
              />
            </label>
            <label>
              Sistema
              <select value={form.gradeSystem} onChange={(e) => set('gradeSystem', e.target.value)}>
                <option value="YDS">YDS (5.x)</option>
                <option value="V">Escala V</option>
                <option value="French">Francesa</option>
                <option value="UK">Clásica UK</option>
                <option value="UIAA">UIAA</option>
              </select>
            </label>
            <label>
              Tipo
              <select value={form.type} onChange={(e) => set('type', e.target.value)}>
                {Object.entries(routeTypeLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <label>
            Descripción
            <textarea
              rows={3}
              placeholder="Reseña, carácter de la vía…"
              value={form.description}
              onChange={(e) => set('description', e.target.value)}
            />
          </label>
          <div className="grid-2">
            <label>
              Etiquetas <span className="hint">(separadas por comas)</span>
              <input
                type="text"
                placeholder="regletas, clásica, resistencia"
                value={form.tags}
                onChange={(e) => set('tags', e.target.value)}
              />
            </label>
            <label>
              Primera ascensión
              <input
                type="text"
                placeholder="Nombre, año"
                value={form.firstAscent}
                onChange={(e) => set('firstAscent', e.target.value)}
              />
            </label>
          </div>
          <div className="modal-actions">
            <button type="button" className="ghost-btn" onClick={onCancel}>
              Cancelar
            </button>
            <button type="submit" className="primary-btn">
              Guardar vía
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
