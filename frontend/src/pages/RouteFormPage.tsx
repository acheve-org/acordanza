import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import * as routesApi from '../api/routes';
import { ApiError } from '../api/client';
import type { RouteDetail, UpsertRouteInput } from '../api/types';
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

export function RouteFormPage() {
  const { id } = useParams();
  const editing = id !== undefined;
  const navigate = useNavigate();

  const [route, setRoute] = useState<RouteDetail | null>(null);
  const [loadError, setLoadError] = useState('');
  const [form, setForm] = useState<UpsertRouteInput>(emptyForm);

  useEffect(() => {
    if (!editing) return;
    routesApi
      .getRoute(Number(id))
      .then((r) => {
        setRoute(r);
        setForm({
          name: r.name,
          crag: r.crag,
          grade: r.grade,
          gradeSystem: r.gradeSystem,
          type: r.type,
          description: r.description,
          tags: r.tags,
          firstAscent: r.firstAscent,
        });
      })
      .catch((err) => {
        setLoadError(err instanceof ApiError ? err.message : 'No se pudo cargar la vía');
      });
  }, [id, editing]);

  if (editing && loadError) {
    return (
      <main className="settings-page">
        <h1>Editar vía</h1>
        <p className="error-text">{loadError}</p>
        <Link to="/" className="ghost-btn small">
          Volver
        </Link>
      </main>
    );
  }

  if (editing && !route) return null;

  function set<K extends keyof UpsertRouteInput>(key: K, value: UpsertRouteInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const input: UpsertRouteInput = {
      ...form,
      name: form.name.trim(),
      crag: form.crag.trim(),
      grade: form.grade.trim(),
      description: form.description.trim(),
      tags: form.tags.trim(),
      firstAscent: form.firstAscent.trim(),
    };
    if (route) {
      await routesApi.updateRoute(route.id, input);
    } else {
      await routesApi.createRoute(input);
    }
    navigate('/');
  }

  return (
    <main className="settings-page">
      <h1>{editing ? 'Editar vía' : 'Añadir una vía'}</h1>
      <section className="settings-section">
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
            <Link to="/" className="ghost-btn">
              Cancelar
            </Link>
            <button type="submit" className="primary-btn">
              Guardar vía
            </button>
          </div>
        </form>
      </section>
    </main>
  );
}
