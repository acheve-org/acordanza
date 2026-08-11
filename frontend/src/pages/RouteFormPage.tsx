import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Button, gradeBand, ModalActions, RemoveButton } from 'acordanza-design-system';
import * as routesApi from '../api/routes';
import { errorMessage } from '../api/errorMessage';
import type { RouteDetail, UpsertRouteInput } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';
import { AreaPicker } from '../components/AreaPicker';
import { FirstAscensionistPicker } from '../components/FirstAscensionistPicker';

// UpsertRouteInput's orientation/gear/description/obligatoryGrade/recommendedGear/
// optionalGear/links/firstAscensionistIds accept null on the wire (the backend DTO
// fields are nullable), but the form itself never holds null for them — it always
// starts from and edits real strings/arrays, only trimmed/assembled into
// UpsertRouteInput on submit.
type RouteFormState = Omit<
  UpsertRouteInput,
  'orientation' | 'gear' | 'description' | 'obligatoryGrade' | 'recommendedGear' | 'optionalGear' | 'links' | 'firstAscensionistIds'
> & {
  orientation: string;
  gear: string;
  description: string;
  obligatoryGrade: string;
  recommendedGear: string;
  optionalGear: string;
  links: string[];
  firstAscensionistIds: number[];
};

const emptyForm: RouteFormState = {
  name: '',
  areaId: 0,
  grade: '',
  orientation: '',
  gear: '',
  description: '',
  length: 0,
  obligatoryGrade: '',
  recommendedGear: '',
  optionalGear: '',
  links: [],
  firstAscensionistIds: [],
};

export function RouteFormPage() {
  const { id } = useParams();
  const editing = id !== undefined;
  const navigate = useNavigate();

  const [route, setRoute] = useState<RouteDetail | null>(null);
  const [loadError, setLoadError] = useState('');
  const [error, setError] = useState('');
  const [form, setForm] = useState<RouteFormState>(emptyForm);

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
          length: r.length,
          obligatoryGrade: r.obligatoryGrade ?? '',
          recommendedGear: r.recommendedGear ?? '',
          optionalGear: r.optionalGear ?? '',
          links: r.links,
          firstAscensionistIds: r.firstAscensionists.map((o) => o.id),
        });
      })
      .catch((err) => {
        setLoadError(errorMessage(err, 'No se pudo cargar la vía'));
      });
  }, [id, editing]);

  if (editing && loadError) {
    return (
      <main className="settings-page">
        <h1>Editar vía</h1>
        <p className="error-text">{loadError}</p>
        <Button as={Link} variant="ghost" small to="/routes">
          Volver
        </Button>
      </main>
    );
  }

  if (editing && !route) return null;

  function set<K extends keyof RouteFormState>(key: K, value: RouteFormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function addLink() {
    setForm((f) => ({ ...f, links: [...f.links, ''] }));
  }

  function updateLink(index: number, value: string) {
    setForm((f) => ({ ...f, links: f.links.map((l, i) => (i === index ? value : l)) }));
  }

  function removeLink(index: number) {
    setForm((f) => ({ ...f, links: f.links.filter((_, i) => i !== index) }));
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (gradeBand(form.grade) === null) {
      setError('el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)');
      return;
    }
    setError('');
    const input: UpsertRouteInput = {
      ...form,
      name: form.name.trim(),
      grade: form.grade.trim().toLowerCase(),
      description: form.description.trim(),
      length: Number(form.length),
      obligatoryGrade: form.obligatoryGrade.trim(),
      recommendedGear: form.recommendedGear.trim(),
      optionalGear: form.optionalGear.trim(),
      orientation: form.orientation || null,
      gear: form.gear || null,
    };
    // Neither response echoes the generated/regenerated slug, so a follow-up GET is the
    // cheapest way to land on the route's own detail URL (plan 21) rather than the plain
    // list — no backend DTO change needed just for this redirect.
    let savedId: number;
    if (route) {
      await routesApi.updateRoute(route.id, input);
      savedId = route.id;
    } else {
      savedId = (await routesApi.createRoute(input)).id;
    }
    const saved = await routesApi.getRoute(savedId);
    navigate(routesApi.routeDetailPath(saved));
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
              Grado <span className="hint">(escala francesa)</span>
              <input
                type="text"
                required
                placeholder="6a, 7c+, 8a…"
                value={form.grade}
                onChange={(e) => set('grade', e.target.value)}
              />
            </label>
          </div>
          <p className="error-text">{error}</p>
          <label>
            Sector
            <AreaPicker value={form.areaId || null} onChange={(areaId) => set('areaId', areaId ?? 0)} allowCreate />
          </label>
          <div className="grid-2">
            <label>
              Longitud <span className="hint">(metros)</span>
              <input
                type="number"
                required
                min={6}
                max={2999}
                step={1}
                value={form.length}
                onChange={(e) => set('length', Number(e.target.value))}
              />
            </label>
            <label>
              Grado obligatorio
              <input
                type="text"
                placeholder="V+, 6a, E2 5b…"
                value={form.obligatoryGrade}
                onChange={(e) => set('obligatoryGrade', e.target.value)}
              />
            </label>
          </div>
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
            Material recomendado
            <input
              type="text"
              placeholder="12 cintas, cuerda de 60 m…"
              value={form.recommendedGear}
              onChange={(e) => set('recommendedGear', e.target.value)}
            />
          </label>
          <label>
            Material opcional
            <input
              type="text"
              placeholder="friends medianos, empotradores…"
              value={form.optionalGear}
              onChange={(e) => set('optionalGear', e.target.value)}
            />
          </label>
          <label>
            Descripción
            <textarea
              rows={3}
              placeholder="Reseña, carácter de la vía…"
              value={form.description}
              onChange={(e) => set('description', e.target.value)}
            />
          </label>
          <div>
            <label>
              Enlaces
              <div className="picker-stack">
                {form.links.map((link, i) => (
                  <div key={i} style={{ display: 'flex', gap: 'var(--space-sm)', alignItems: 'center' }}>
                    <input
                      type="url"
                      placeholder="https://…"
                      value={link}
                      onChange={(e) => updateLink(i, e.target.value)}
                    />
                    <RemoveButton label="Eliminar enlace" onClick={() => removeLink(i)} />
                  </div>
                ))}
              </div>
            </label>
            <Button variant="ghost" small type="button" style={{ marginTop: 'var(--space-xs)' }} onClick={addLink}>
              + Añadir enlace
            </Button>
          </div>
          <label>
            Apertura
            <FirstAscensionistPicker
              value={form.firstAscensionistIds}
              onChange={(firstAscensionistIds) => set('firstAscensionistIds', firstAscensionistIds)}
            />
          </label>
          <ModalActions>
            <Button as={Link} variant="ghost" to="/routes">
              Cancelar
            </Button>
            <Button type="submit" disabled={!form.areaId}>
              Guardar vía
            </Button>
          </ModalActions>
        </form>
      </section>
    </main>
  );
}
