import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Link, Navigate } from 'react-router';
import { MapContainer, Polygon, TileLayer, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet-draw';
import 'leaflet/dist/leaflet.css';
import 'leaflet-draw/dist/leaflet.draw.css';
import * as areasApi from '../api/areas';
import { areaPath } from '../api/areas';
import type { AreaSummary, GeoPoint } from '../api/types';
import { Permissions } from '../api/permissions';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { AreaPicker } from '../components/AreaPicker';

const SPAIN_CENTER: [number, number] = [40.4168, -3.7038];
const SPAIN_ZOOM = 6;
const DRAW_ZOOM = 12;

function FlyToArea({ area }: { area: AreaSummary | undefined }) {
  const map = useMap();

  useEffect(() => {
    if (!area || area.centroidLat === null || area.centroidLng === null) return;
    map.flyTo([area.centroidLat, area.centroidLng], Math.max(map.getZoom(), DRAW_ZOOM));
  }, [area, map]);

  return null;
}

function PolygonDrawer({ onCreated }: { onCreated: (points: GeoPoint[]) => void }) {
  const map = useMap();

  useEffect(() => {
    const drawer = new L.Draw.Polygon(map as unknown as L.DrawMap, {
      shapeOptions: { color: '#e67e22' },
      showArea: false,
    });
    drawer.enable();

    function handleCreated(e: L.LeafletEvent) {
      const layer = (e as L.DrawEvents.Created).layer as L.Polygon;
      const latlngs = (layer.getLatLngs()[0] as L.LatLng[]).map((ll) => ({ lat: ll.lat, lng: ll.lng }));
      onCreated(latlngs);
    }

    map.on(L.Draw.Event.CREATED, handleCreated);
    return () => {
      map.off(L.Draw.Event.CREATED, handleCreated);
      drawer.disable();
    };
  }, [map, onCreated]);

  return null;
}

export function AreasPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.areaEdit);
  const canDelete = hasPermission(Permissions.areaDelete);

  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [error, setError] = useState('');

  const [newName, setNewName] = useState('');
  const [newParentId, setNewParentId] = useState<number | null>(null);

  const [editingId, setEditingId] = useState<number | null>(null);
  const [editName, setEditName] = useState('');
  const [editParentId, setEditParentId] = useState<number | null>(null);

  const [drawAreaId, setDrawAreaId] = useState<number | null>(null);
  const [drawing, setDrawing] = useState(false);
  const [pendingPolygon, setPendingPolygon] = useState<GeoPoint[] | null>(null);

  function refresh() {
    return areasApi.listAreas().then(setAreas);
  }

  useEffect(() => {
    if (!canEdit) return;
    refresh();
    // Only meant to run once, gated on the permission being available.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canEdit]);

  const byId = useMemo(() => new Map(areas.map((a) => [a.id, a])), [areas]);
  const sortedAreas = useMemo(
    () => [...areas].sort((a, b) => areaPath(a, byId).localeCompare(areaPath(b, byId))),
    [areas, byId],
  );
  const drawArea = drawAreaId !== null ? byId.get(drawAreaId) : undefined;

  if (!canEdit) return <Navigate to="/" replace />;

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    const name = newName.trim();
    if (!name) return;
    setError('');
    try {
      await areasApi.createArea(name, newParentId);
      setNewName('');
      setNewParentId(null);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo crear el sector');
    }
  }

  function startEdit(area: AreaSummary) {
    setEditingId(area.id);
    setEditName(area.name);
    setEditParentId(area.parentAreaId);
    setError('');
  }

  async function handleSaveEdit(e: FormEvent) {
    e.preventDefault();
    if (editingId === null) return;
    const name = editName.trim();
    if (!name) return;
    setError('');
    try {
      await areasApi.updateArea(editingId, name, editParentId);
      setEditingId(null);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo editar el sector');
    }
  }

  async function handleDelete(area: AreaSummary) {
    if (!confirm(`¿Eliminar el sector "${area.name}"?`)) return;
    setError('');
    try {
      await areasApi.deleteArea(area.id);
      if (drawAreaId === area.id) setDrawAreaId(null);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo eliminar el sector');
    }
  }

  function handleSelectDrawArea(id: number | null) {
    setDrawAreaId(id);
    setPendingPolygon(null);
    setDrawing(false);
    setError('');
  }

  async function handleSavePolygon() {
    if (drawAreaId === null || !pendingPolygon) return;
    try {
      await areasApi.updateAreaCoordinates(drawAreaId, pendingPolygon);
      setPendingPolygon(null);
      setError('');
      await refresh();
    } catch {
      setError('No se pudo guardar el polígono');
    }
  }

  function handleCancelDraw() {
    setPendingPolygon(null);
    setDrawing(false);
  }

  return (
    <main className="areas-page">
      <h1>Gestionar sectores</h1>

      <section className="settings-section">
        <h2>Añadir sector</h2>
        <form onSubmit={handleCreate} className="passkey-form">
          <label>
            Nombre
            <input type="text" required value={newName} onChange={(e) => setNewName(e.target.value)} />
          </label>
          <label>
            Sector padre
            <select
              value={newParentId ?? ''}
              onChange={(e) => setNewParentId(e.target.value ? Number(e.target.value) : null)}
            >
              <option value="">Sin padre (sector principal)</option>
              {sortedAreas.map((a) => (
                <option key={a.id} value={a.id}>
                  {areaPath(a, byId)}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" className="primary-btn">
            Añadir
          </button>
        </form>
      </section>

      <section className="settings-section">
        <h2>Sectores</h2>
        {sortedAreas.length === 0 ? (
          <p className="empty-state">No hay sectores todavía</p>
        ) : (
          <div className="users-table-wrap">
            <table className="users-table">
              <thead>
                <tr>
                  <th>Nombre</th>
                  <th>Sector padre</th>
                  <th>Polígono</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {sortedAreas.map((a) =>
                  editingId === a.id ? (
                    <tr key={a.id}>
                      <td colSpan={4}>
                        <form onSubmit={handleSaveEdit} className="grid-2">
                          <label>
                            Nombre
                            <input type="text" required value={editName} onChange={(e) => setEditName(e.target.value)} />
                          </label>
                          <label>
                            Sector padre
                            <select
                              value={editParentId ?? ''}
                              onChange={(e) => setEditParentId(e.target.value ? Number(e.target.value) : null)}
                            >
                              <option value="">Sin padre (sector principal)</option>
                              {sortedAreas
                                .filter((o) => o.id !== a.id)
                                .map((o) => (
                                  <option key={o.id} value={o.id}>
                                    {areaPath(o, byId)}
                                  </option>
                                ))}
                            </select>
                          </label>
                          <div className="modal-actions">
                            <button type="button" className="ghost-btn small" onClick={() => setEditingId(null)}>
                              Cancelar
                            </button>
                            <button type="submit" className="primary-btn">
                              Guardar
                            </button>
                          </div>
                        </form>
                      </td>
                    </tr>
                  ) : (
                    <tr key={a.id}>
                      <td>{a.name}</td>
                      <td>{a.parentAreaId !== null ? (byId.get(a.parentAreaId)?.name ?? '—') : '—'}</td>
                      <td>{a.polygon.length > 0 ? `${a.polygon.length} vértices` : 'Sin polígono'}</td>
                      <td>
                        <div className="detail-actions">
                          <button type="button" className="ghost-btn small" onClick={() => startEdit(a)}>
                            Editar
                          </button>
                          {canDelete && (
                            <button type="button" className="ghost-btn small danger" onClick={() => handleDelete(a)}>
                              Eliminar
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="settings-section">
        <h2>Dibujar polígono</h2>
        <div className="area-map-wrap">
          <div className="area-map-editor">
            <AreaPicker value={drawAreaId} onChange={handleSelectDrawArea} allowClear />
            {drawArea && !drawing && !pendingPolygon && (
              <button type="button" className="ghost-btn small" onClick={() => setDrawing(true)}>
                {drawArea.polygon.length > 0 ? 'Redibujar polígono' : 'Dibujar polígono'}
              </button>
            )}
            {drawing && (
              <p className="hint">Haz clic para colocar los vértices y doble clic para cerrar el polígono.</p>
            )}
            {pendingPolygon && (
              <div className="modal-actions">
                <button type="button" className="ghost-btn small" onClick={handleCancelDraw}>
                  Cancelar
                </button>
                <button type="button" className="primary-btn" onClick={handleSavePolygon}>
                  Guardar polígono
                </button>
              </div>
            )}
          </div>

          <MapContainer center={SPAIN_CENTER} zoom={SPAIN_ZOOM} className="area-map">
            <TileLayer
              attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
              url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
            />
            <FlyToArea area={drawArea} />

            {drawArea && drawArea.polygon.length > 0 && !pendingPolygon && (
              <Polygon positions={drawArea.polygon.map((p) => [p.lat, p.lng])} pathOptions={{ color: '#38a169' }} />
            )}

            {drawArea && drawing && (
              <PolygonDrawer
                onCreated={(points) => {
                  setPendingPolygon(points);
                  setDrawing(false);
                }}
              />
            )}

            {drawArea && pendingPolygon && (
              <Polygon
                positions={pendingPolygon.map((p) => [p.lat, p.lng])}
                pathOptions={{ color: '#e67e22', dashArray: '4' }}
              />
            )}
          </MapContainer>
        </div>
      </section>

      {error && <p className="error-text">{error}</p>}

      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
  );
}
