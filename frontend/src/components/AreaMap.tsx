import { useEffect, useMemo, useRef, useState } from 'react';
import { MapContainer, Marker, Polygon, Popup, TileLayer, useMapEvents } from 'react-leaflet';
import MarkerClusterGroup from 'react-leaflet-cluster';
import { Link } from 'react-router';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import 'leaflet.markercluster/dist/MarkerCluster.css';
import 'leaflet.markercluster/dist/MarkerCluster.Default.css';
import markerIcon2x from 'leaflet/dist/images/marker-icon-2x.png';
import markerIcon from 'leaflet/dist/images/marker-icon.png';
import markerShadow from 'leaflet/dist/images/marker-shadow.png';
import { Button } from 'acordanza-design-system';
import { deriveVisibleAreas, DEPTH_COLORS, SUBAREA_ZOOM } from '../lib/exploreAreas';
import type { AreaBounds, AreaSummary, GeoPoint } from '../api/types';

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

export interface FlyTarget {
  bounds?: [[number, number], [number, number]];
  center?: [number, number];
  zoom?: number;
}

function centroidOf(area: AreaSummary): GeoPoint | null {
  return area.centroidLat !== null && area.centroidLng !== null
    ? { lat: area.centroidLat, lng: area.centroidLng }
    : null;
}

function BoundsTracker({ onChange }: { onChange: (bounds: AreaBounds, zoom: number) => void }) {
  const map = useMapEvents({
    moveend: () => {
      const b = map.getBounds();
      onChange({ minLat: b.getSouth(), minLng: b.getWest(), maxLat: b.getNorth(), maxLng: b.getEast() }, map.getZoom());
    },
    zoomend: () => {
      const b = map.getBounds();
      onChange({ minLat: b.getSouth(), minLng: b.getWest(), maxLat: b.getNorth(), maxLng: b.getEast() }, map.getZoom());
    },
  });

  useEffect(() => {
    const b = map.getBounds();
    onChange({ minLat: b.getSouth(), minLng: b.getWest(), maxLat: b.getNorth(), maxLng: b.getEast() }, map.getZoom());
    // Only meant to run once, when the map first mounts.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return null;
}

export interface AreaMapProps {
  areas: AreaSummary[];
  onBoundsChange: (bounds: AreaBounds, zoom: number) => void;
  selectedAreaId: number | null;
  onSelectArea: (id: number | null) => void;
  flyTarget: FlyTarget | null;
  onLocateMe: () => void;
  locating: boolean;
}

export function AreaMap({
  areas,
  onBoundsChange,
  selectedAreaId,
  onSelectArea,
  flyTarget,
  onLocateMe,
  locating,
}: AreaMapProps) {
  const [center, setCenter] = useState<L.LatLng | null>(null);
  const [zoom, setZoom] = useState(SPAIN_ZOOM);
  const mapRef = useRef<L.Map | null>(null);
  const markerRefs = useRef(new Map<number, L.Marker>());
  const polygonRefs = useRef(new Map<number, L.Polygon>());

  function handleBoundsChange(bounds: AreaBounds, mapZoom: number) {
    setCenter(L.latLng((bounds.minLat + bounds.maxLat) / 2, (bounds.minLng + bounds.maxLng) / 2));
    setZoom(mapZoom);
    onBoundsChange(bounds, mapZoom);
  }

  const { topLevelAreas, activeTopLevelArea, subareas } = useMemo(
    () => deriveVisibleAreas(areas, center, zoom),
    [areas, center, zoom],
  );

  useEffect(() => {
    if (!flyTarget || !mapRef.current) return;
    if (flyTarget.bounds) mapRef.current.fitBounds(flyTarget.bounds, { padding: [40, 40] });
    else if (flyTarget.center) mapRef.current.flyTo(flyTarget.center, flyTarget.zoom ?? SUBAREA_ZOOM);
  }, [flyTarget]);

  useEffect(() => {
    if (selectedAreaId == null) return;
    markerRefs.current.get(selectedAreaId)?.openPopup();
    polygonRefs.current.get(selectedAreaId)?.openPopup();
  }, [selectedAreaId, topLevelAreas, subareas]);

  return (
    <div className="area-map-wrap">
      <MapContainer ref={mapRef} center={SPAIN_CENTER} zoom={SPAIN_ZOOM} maxZoom={17} className="area-map">
        <TileLayer
          attribution='Datos: &copy; colaboradores de <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>, SRTM | Mapa: &copy; <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)'
          url="https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png"
        />
        <BoundsTracker onChange={handleBoundsChange} />

        <MarkerClusterGroup>
          {topLevelAreas.map((area) => {
            const centroid = centroidOf(area);
            if (!centroid) return null;
            return (
              <Marker
                key={area.id}
                position={[centroid.lat, centroid.lng]}
                ref={(marker) => {
                  if (marker) markerRefs.current.set(area.id, marker);
                  else markerRefs.current.delete(area.id);
                }}
                eventHandlers={{ click: () => onSelectArea(area.id) }}
              >
                <Popup>
                  <div className="area-map-popup">
                    <span>{area.name}</span>
                    <Button as={Link} variant="ghost" small to={`/areas/${area.slug}`}>
                      Ver
                    </Button>
                  </div>
                </Popup>
              </Marker>
            );
          })}
        </MarkerClusterGroup>

        {activeTopLevelArea && activeTopLevelArea.polygon.length > 0 && (
          <Polygon
            positions={activeTopLevelArea.polygon.map((p): [number, number] => [p.lat, p.lng])}
            pathOptions={{ color: DEPTH_COLORS.root }}
          />
        )}

        {subareas.map((area) =>
          area.polygon.length > 0 ? (
            <Polygon
              key={area.id}
              positions={area.polygon.map((p): [number, number] => [p.lat, p.lng])}
              pathOptions={{ color: DEPTH_COLORS.subarea }}
              ref={(polygon) => {
                if (polygon) polygonRefs.current.set(area.id, polygon);
                else polygonRefs.current.delete(area.id);
              }}
              eventHandlers={{ click: () => onSelectArea(area.id) }}
            >
              <Popup>
                <div className="area-map-popup">
                  <span>{area.name}</span>
                  <Button as={Link} variant="ghost" small to={`/areas/${activeTopLevelArea!.slug}/${area.slug}`}>
                    Ver
                  </Button>
                </div>
              </Popup>
            </Polygon>
          ) : null,
        )}
      </MapContainer>

      <button
        type="button"
        className="area-map-locate"
        onClick={onLocateMe}
        disabled={locating}
        aria-label="Ir a mi ubicación"
        title="Ir a mi ubicación"
      >
        {locating ? (
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="12" r="9" strokeOpacity="0.25" />
            <path d="M21 12a9 9 0 0 0-9-9" />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="12" r="3" />
            <path d="M12 2v3M12 19v3M2 12h3M19 12h3" />
          </svg>
        )}
      </button>
    </div>
  );
}
