import { useEffect, useMemo, useState } from 'react';
import { Link, Navigate } from 'react-router';
import { MapContainer, Polygon, TileLayer, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet-draw';
import 'leaflet/dist/leaflet.css';
import 'leaflet-draw/dist/leaflet.draw.css';
import * as areasApi from '../api/areas';
import type { AreaSummary, GeoPoint } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { AreaPicker } from '../components/AreaPicker';
import { AreaTree } from '../components/AreaTree';

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
  const canCreate = hasPermission(Permissions.areaCreate);
  const canDelete = hasPermission(Permissions.areaDelete);

  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [areaVersion, setAreaVersion] = useState(0);
  const [error, setError] = useState('');

  const [drawAreaId, setDrawAreaId] = useState<number | null>(null);
  const [drawing, setDrawing] = useState(false);
  const [pendingPolygon, setPendingPolygon] = useState<GeoPoint[] | null>(null);

  // Also used as AreaTree's onChange: creates/deletes there don't touch this page's own
  // `areas` state, so without this the polygon section (and AreaPicker's own list) could
  // keep showing/selecting an area the tree just deleted.
  function refresh() {
    return areasApi.listAreas().then((list) => {
      setAreas(list);
      setAreaVersion((v) => v + 1);
      if (drawAreaId !== null && !list.some((a) => a.id === drawAreaId)) {
        setDrawAreaId(null);
        setPendingPolygon(null);
        setDrawing(false);
      }
    });
  }

  useEffect(() => {
    if (!canEdit) return;
    refresh();
    // Only meant to run once, gated on the permission being available.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canEdit]);

  const byId = useMemo(() => new Map(areas.map((a) => [a.id, a])), [areas]);
  const drawArea = drawAreaId !== null ? byId.get(drawAreaId) : undefined;

  if (!canEdit) return <Navigate to="/" replace />;

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
        <h2>Sectores</h2>
        <AreaTree canCreate={canCreate} canDelete={canDelete} onChange={refresh} />
      </section>

      <section className="settings-section">
        <h2>Dibujar polígono</h2>
        <div className="area-map-wrap">
          <div className="area-map-editor">
            <AreaPicker value={drawAreaId} onChange={handleSelectDrawArea} allowClear refreshToken={areaVersion} />
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
