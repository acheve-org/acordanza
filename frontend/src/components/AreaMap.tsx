import { useEffect, useMemo, useState } from 'react';
import { MapContainer, Marker, Polygon, Popup, TileLayer, useMap, useMapEvents } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet-draw';
import 'leaflet/dist/leaflet.css';
import 'leaflet-draw/dist/leaflet.draw.css';
import markerIcon2x from 'leaflet/dist/images/marker-icon-2x.png';
import markerIcon from 'leaflet/dist/images/marker-icon.png';
import markerShadow from 'leaflet/dist/images/marker-shadow.png';
import * as areasApi from '../api/areas';
import type { AreaSummary, GeoPoint } from '../api/types';
import { AreaPicker } from './AreaPicker';

// Vite bundles Leaflet's default marker icon paths incorrectly unless pointed
// at the imported asset URLs explicitly; the prototype method must go too, or
// it keeps building URLs from the auto-detected (wrong) image path instead.
delete (L.Icon.Default.prototype as { _getIconUrl?: unknown })._getIconUrl;
L.Icon.Default.mergeOptions({
  iconRetinaUrl: markerIcon2x,
  iconUrl: markerIcon,
  shadowUrl: markerShadow,
});

const SPAIN_CENTER: [number, number] = [40.4168, -3.7038];
const SPAIN_ZOOM = 6;
// Below this zoom, only top-level area markers are shown; at or above it, the
// nearest top-level area's subareas are revealed as polygons.
const SUBAREA_ZOOM = 12;
// Degrees of lat/lng — beyond this, no top-level area is considered "in view"
// even if it's the nearest one, so panning far away clears the subarea view.
const MAX_ACTIVE_DISTANCE = 0.5;

function centroidOf(area: AreaSummary): GeoPoint | null {
  return area.centroidLat !== null && area.centroidLng !== null
    ? { lat: area.centroidLat, lng: area.centroidLng }
    : null;
}

function ViewTracker({ onChange }: { onChange: (center: L.LatLng, zoom: number) => void }) {
  const map = useMapEvents({
    moveend: () => onChange(map.getCenter(), map.getZoom()),
    zoomend: () => onChange(map.getCenter(), map.getZoom()),
  });

  useEffect(() => {
    onChange(map.getCenter(), map.getZoom());
    // Only meant to run once, when the map first mounts.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return null;
}

function FlyToArea({ area }: { area: AreaSummary | undefined }) {
  const map = useMap();

  useEffect(() => {
    if (!area) return;
    const centroid = centroidOf(area);
    if (!centroid) return;
    map.flyTo([centroid.lat, centroid.lng], Math.max(map.getZoom(), SUBAREA_ZOOM));
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

interface Props {
  canEdit: boolean;
}

export function AreaMap({ canEdit }: Props) {
  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [center, setCenter] = useState<L.LatLng | null>(null);
  const [zoom, setZoom] = useState(SPAIN_ZOOM);
  const [editingAreaId, setEditingAreaId] = useState<number | null>(null);
  const [drawing, setDrawing] = useState(false);
  const [pendingPolygon, setPendingPolygon] = useState<GeoPoint[] | null>(null);
  const [error, setError] = useState('');

  function refresh() {
    return areasApi.listAreas().then(setAreas);
  }

  useEffect(() => {
    refresh();
  }, []);

  const byId = useMemo(() => new Map(areas.map((a) => [a.id, a])), [areas]);
  const topLevelAreas = useMemo(() => areas.filter((a) => a.parentAreaId === null), [areas]);

  const activeTopLevelArea = useMemo(() => {
    if (!center || zoom < SUBAREA_ZOOM) return null;
    let closest: AreaSummary | null = null;
    let bestDistance = Infinity;
    for (const area of topLevelAreas) {
      const centroid = centroidOf(area);
      if (!centroid) continue;
      const distance = Math.hypot(centroid.lat - center.lat, centroid.lng - center.lng);
      if (distance < bestDistance) {
        bestDistance = distance;
        closest = area;
      }
    }
    return bestDistance <= MAX_ACTIVE_DISTANCE ? closest : null;
  }, [topLevelAreas, center, zoom]);

  const subareas = useMemo(
    () => (activeTopLevelArea ? areas.filter((a) => a.parentAreaId === activeTopLevelArea.id) : []),
    [areas, activeTopLevelArea],
  );

  const editingArea = editingAreaId !== null ? byId.get(editingAreaId) : undefined;

  function handleSelectEditingArea(id: number | null) {
    setEditingAreaId(id);
    setPendingPolygon(null);
    setDrawing(false);
    setError('');
  }

  async function handleSavePolygon() {
    if (editingAreaId === null || !pendingPolygon) return;
    try {
      await areasApi.updateAreaCoordinates(editingAreaId, pendingPolygon);
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
    <div className="area-map-wrap">
      {canEdit && (
        <div className="area-map-editor">
          <AreaPicker value={editingAreaId} onChange={handleSelectEditingArea} allowClear />
          {editingArea && !drawing && !pendingPolygon && (
            <button type="button" className="ghost-btn small" onClick={() => setDrawing(true)}>
              {editingArea.polygon.length > 0 ? 'Redibujar polígono' : 'Dibujar polígono'}
            </button>
          )}
          {drawing && <p className="hint">Haz clic para colocar los vértices y doble clic para cerrar el polígono.</p>}
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
          {error && <p className="error-text">{error}</p>}
        </div>
      )}

      <MapContainer center={SPAIN_CENTER} zoom={SPAIN_ZOOM} className="area-map">
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />
        <ViewTracker onChange={(c, z) => { setCenter(c); setZoom(z); }} />
        {canEdit && <FlyToArea area={editingArea} />}

        {topLevelAreas.map((area) => {
          const centroid = centroidOf(area);
          if (!centroid) return null;
          return (
            <Marker key={area.id} position={[centroid.lat, centroid.lng]}>
              <Popup>{area.name}</Popup>
            </Marker>
          );
        })}

        {activeTopLevelArea && activeTopLevelArea.polygon.length > 0 && (
          <Polygon
            positions={activeTopLevelArea.polygon.map((p) => [p.lat, p.lng])}
            pathOptions={{ color: '#3182ce' }}
          />
        )}

        {subareas.map((area) =>
          area.polygon.length > 0 ? (
            <Polygon key={area.id} positions={area.polygon.map((p) => [p.lat, p.lng])} pathOptions={{ color: '#38a169' }}>
              <Popup>{area.name}</Popup>
            </Polygon>
          ) : null,
        )}

        {editingArea && drawing && <PolygonDrawer onCreated={(points) => { setPendingPolygon(points); setDrawing(false); }} />}

        {editingArea && pendingPolygon && (
          <Polygon
            positions={pendingPolygon.map((p) => [p.lat, p.lng])}
            pathOptions={{ color: '#e67e22', dashArray: '4' }}
          />
        )}
      </MapContainer>
    </div>
  );
}
