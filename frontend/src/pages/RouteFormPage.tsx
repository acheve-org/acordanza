import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import * as routesApi from '../api/routes';
import { ApiError } from '../api/client';
import type { RouteDetail, UpsertRouteInput } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';
import { AreaPicker } from '../components/AreaPicker';
import { FirstAscensionistPicker } from '../components/FirstAscensionistPicker';

const emptyForm: UpsertRouteInput = {
  name: '',
  areaId: 0,
  grade: '',
  orientation: '',
  gear: '',
  description: '',
  tags: '',
  firstAscensionistIds: [],
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
          areaId: r.areaId,
          grade: r.grade,
          orientation: r.orientation ?? '',
          gear: r.gear ?? '',
          description: r.description,
          tags: r.tags,
          firstAscensionistIds: r.firstAscensionists.map((o) => o.id),
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
      grade: form.grade.trim(),
      description: form.description.trim(),
      tags: form.tags.trim(),
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
              Grado
              <input
                type="text"
                required
                placeholder="V+, 6a, E2 5b…"
                value={form.grade}
                onChange={(e) => set('grade', e.target.value)}
              />
            </label>
          </div>
          <label>
            Sector
            <AreaPicker value={form.areaId || null} onChange={(areaId) => set('areaId', areaId ?? 0)} allowCreate />
          </label>
          <div className="grid-2">
            <label>
              Orientación
              <select value={form.orientation} onChange={(e) => set('orientation', e.target.value)}>
                <option value="">Sin especificar</option>
                {Object.entries(orientationLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Equipamiento
              <select value={form.gear} onChange={(e) => set('gear', e.target.value)}>
                <option value="">Sin especificar</option>
                {Object.entries(gearLabels).map(([value, label]) => (
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
            Apertura
            <FirstAscensionistPicker
              value={form.firstAscensionistIds}
              onChange={(firstAscensionistIds) => set('firstAscensionistIds', firstAscensionistIds)}
            />
          </label>
          <div className="modal-actions">
            <Link to="/" className="ghost-btn">
              Cancelar
            </Link>
            <button type="submit" className="primary-btn" disabled={!form.areaId}>
              Guardar vía
            </button>
          </div>
        </form>
      </section>
    </main>
  );
}
