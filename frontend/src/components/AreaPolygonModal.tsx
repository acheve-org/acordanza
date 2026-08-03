import { useEffect, useState } from 'react';
import { MapContainer, Polygon, TileLayer, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet-draw';
import 'leaflet/dist/leaflet.css';
import 'leaflet-draw/dist/leaflet.draw.css';
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
  area: AreaSummary;
  parentPolygon: GeoPoint[] | null;
  onClose: () => void;
  onSaved: () => void;
}

export function AreaPolygonModal({ area, parentPolygon, onClose, onSaved }: Props) {
  const [drawing, setDrawing] = useState(false);
  const [pendingPolygon, setPendingPolygon] = useState<GeoPoint[] | null>(null);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const hasPolygon = area.polygon.length > 0;

  async function handleSave() {
    if (!pendingPolygon) return;
    setSaving(true);
    setError('');
    try {
      await areasApi.updateAreaCoordinates(area.id, pendingPolygon);
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo guardar el polígono');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal modal-wide">
        <h2>{hasPolygon ? 'Editar polígono' : 'Dibujar polígono'} — {area.name}</h2>

        <div className="area-map-editor">
          {!drawing && !pendingPolygon && (
            <button type="button" className="ghost-btn small" onClick={() => setDrawing(true)}>
              {hasPolygon ? 'Redibujar polígono' : 'Dibujar polígono'}
            </button>
          )}
          {drawing && <p className="hint">Haz clic para colocar los vértices y doble clic para cerrar el polígono.</p>}
          {pendingPolygon && (
            <div className="modal-actions">
              <button type="button" className="ghost-btn small" onClick={() => setPendingPolygon(null)}>
                Cancelar
              </button>
              <button type="button" className="primary-btn" disabled={saving} onClick={handleSave}>
                {saving ? 'Guardando…' : 'Guardar polígono'}
              </button>
            </div>
          )}
        </div>

        <MapContainer center={SPAIN_CENTER} zoom={SPAIN_ZOOM} className="area-map">
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <InitialView ownPolygon={area.polygon} parentPolygon={parentPolygon} />

          {area.polygon.length === 0 && parentPolygon && parentPolygon.length > 0 && (
            <Polygon positions={toLatLngs(parentPolygon)} pathOptions={{ color: '#8892a0', dashArray: '4', fill: false }} />
          )}

          {hasPolygon && !pendingPolygon && (
            <Polygon positions={toLatLngs(area.polygon)} pathOptions={{ color: '#38a169' }} />
          )}

          {drawing && <PolygonDrawer onCreated={(points) => { setPendingPolygon(points); setDrawing(false); }} />}

          {pendingPolygon && <Polygon positions={toLatLngs(pendingPolygon)} pathOptions={{ color: '#e67e22', dashArray: '4' }} />}
        </MapContainer>

        {error && <p className="error-text">{error}</p>}

        <div className="modal-actions">
          <button type="button" className="ghost-btn" onClick={onClose}>
            Cerrar
          </button>
        </div>
      </div>
    </div>
  );
}
