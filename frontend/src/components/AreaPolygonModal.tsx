import { useEffect, useState } from 'react';
import { MapContainer, Polygon, TileLayer, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet-draw';
import 'leaflet/dist/leaflet.css';
import 'leaflet-draw/dist/leaflet.draw.css';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import type { AreaSummary, GeoPoint } from '../api/types';
import { ApiError } from '../api/client';

const SPAIN_CENTER: [number, number] = [40.4168, -3.7038];
const SPAIN_ZOOM = 6;

function toLatLngs(points: GeoPoint[]): [number, number][] {
  return points.map((p) => [p.lat, p.lng]);
}

// Frames the map on mount: the area's own polygon if it has one, otherwise the parent's
// polygon (per issue #58 — helps place a new subarea inside its parent's boundary), otherwise
// the default Spain-wide view. Deliberately runs once, not on every polygon/parent change.
function InitialView({ ownPolygon, parentPolygon }: { ownPolygon: GeoPoint[]; parentPolygon: GeoPoint[] | null }) {
  const map = useMap();

  useEffect(() => {
    const target = ownPolygon.length > 0 ? ownPolygon : parentPolygon && parentPolygon.length > 0 ? parentPolygon : null;
    if (target) {
      map.fitBounds(L.latLngBounds(toLatLngs(target)), { padding: [40, 40] });
    } else {
      map.setView(SPAIN_CENTER, SPAIN_ZOOM);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map]);

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

interface Props {
  areaId: number;
  areaName: string;
  parentAreaId: number | null;
  onClose: () => void;
  onSaved: () => void;
}

export function AreaPolygonModal({ areaId, areaName, parentAreaId, onClose, onSaved }: Props) {
  // Vertex data is only worth fetching once this modal is open — it's the only screen that
  // draws polygons, so the area tree behind it deliberately doesn't carry any.
  const [polygon, setPolygon] = useState<GeoPoint[] | null>(null);
  const [parentPolygon, setParentPolygon] = useState<GeoPoint[] | null>(null);
  const [drawing, setDrawing] = useState(false);
  const [pendingPolygon, setPendingPolygon] = useState<GeoPoint[] | null>(null);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let cancelled = false;
    areasApi
      .listAreas(undefined, true)
      .then((areas) => {
        if (cancelled) return;
        const byId = new Map<number, AreaSummary>(areas.map((a) => [a.id, a]));
        setPolygon(byId.get(areaId)?.polygon ?? []);
        setParentPolygon(parentAreaId !== null ? byId.get(parentAreaId)?.polygon ?? null : null);
      })
      .catch(() => {
        if (cancelled) return;
        // Fall back to a polygon-less area so drawing — the only entry point for creating
        // a boundary — stays usable instead of the modal being stuck on "Cargando…".
        setPolygon([]);
        setError('No se pudieron cargar los polígonos existentes');
      });
    return () => {
      cancelled = true;
    };
  }, [areaId, parentAreaId]);

  const hasPolygon = polygon !== null && polygon.length > 0;

  async function handleSave() {
    if (!pendingPolygon) return;
    setSaving(true);
    setError('');
    try {
      await areasApi.updateAreaCoordinates(areaId, pendingPolygon);
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo guardar el polígono');
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal onClose={onClose} wide>
      <h2>{hasPolygon ? 'Editar polígono' : 'Dibujar polígono'} — {areaName}</h2>

      {/* The map is held back until the polygons land: InitialView frames the view once,
          on mount, so mounting it early would leave it stuck on the Spain-wide default. */}
      {polygon === null ? (
        <p className="hint">Cargando…</p>
      ) : (
        <>
          <div className="area-map-editor">
            {!drawing && !pendingPolygon && (
              <Button variant="ghost" small type="button" onClick={() => setDrawing(true)}>
                {hasPolygon ? 'Redibujar polígono' : 'Dibujar polígono'}
              </Button>
            )}
            {drawing && <p className="hint">Haz clic para colocar los vértices y doble clic para cerrar el polígono.</p>}
            {pendingPolygon && (
              <ModalActions>
                <Button variant="ghost" small type="button" onClick={() => setPendingPolygon(null)}>
                  Cancelar
                </Button>
                <Button type="button" disabled={saving} onClick={handleSave}>
                  {saving ? 'Guardando…' : 'Guardar polígono'}
                </Button>
              </ModalActions>
            )}
          </div>

          <MapContainer center={SPAIN_CENTER} zoom={SPAIN_ZOOM} maxZoom={17} className="area-map">
            <TileLayer
              attribution='Datos: &copy; colaboradores de <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>, SRTM | Mapa: &copy; <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)'
              url="https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png"
            />
            <InitialView ownPolygon={polygon} parentPolygon={parentPolygon} />

            {polygon.length === 0 && parentPolygon && parentPolygon.length > 0 && (
              <Polygon positions={toLatLngs(parentPolygon)} pathOptions={{ color: '#8892a0', dashArray: '4', fill: false }} />
            )}

            {hasPolygon && !pendingPolygon && (
              <Polygon positions={toLatLngs(polygon)} pathOptions={{ color: '#38a169' }} />
            )}

            {drawing && <PolygonDrawer onCreated={(points) => { setPendingPolygon(points); setDrawing(false); }} />}

            {pendingPolygon && <Polygon positions={toLatLngs(pendingPolygon)} pathOptions={{ color: '#e67e22', dashArray: '4' }} />}
          </MapContainer>
        </>
      )}

      {error && <p className="error-text">{error}</p>}

      <ModalActions>
        <Button variant="ghost" type="button" onClick={onClose}>
          Cerrar
        </Button>
      </ModalActions>
    </Modal>
  );
}
