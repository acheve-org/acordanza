import { useEffect, useMemo, useRef, useState } from 'react';
import { MapContainer, Marker, Polygon, Popup, TileLayer, useMapEvents } from 'react-leaflet';
import { Link } from 'react-router';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import markerIcon2x from 'leaflet/dist/images/marker-icon-2x.png';
import markerIcon from 'leaflet/dist/images/marker-icon.png';
import markerShadow from 'leaflet/dist/images/marker-shadow.png';
import { Button } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
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
// Below this zoom, only top-level area markers are shown; at or above it, the
// nearest top-level area's subareas are revealed as polygons.
const SUBAREA_ZOOM = 12;
const BOUNDS_FETCH_DEBOUNCE_MS = 300;

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

export function AreaMap() {
  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [center, setCenter] = useState<L.LatLng | null>(null);
  const [zoom, setZoom] = useState(SPAIN_ZOOM);
  const fetchHandle = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Bumped on every fetch actually sent, so a slow/out-of-order response can
  // recognize it's stale (a newer fetch has since started) and skip setAreas.
  const requestId = useRef(0);

  function handleBoundsChange(bounds: AreaBounds, mapZoom: number) {
    setCenter(L.latLng((bounds.minLat + bounds.maxLat) / 2, (bounds.minLng + bounds.maxLng) / 2));
    setZoom(mapZoom);

    if (fetchHandle.current) clearTimeout(fetchHandle.current);
    fetchHandle.current = setTimeout(() => {
      const thisRequestId = ++requestId.current;
      areasApi.listAreas(bounds).then((fetched) => {
        if (thisRequestId === requestId.current) setAreas(fetched);
      });
    }, BOUNDS_FETCH_DEBOUNCE_MS);
  }

  useEffect(() => {
    return () => {
      if (fetchHandle.current) clearTimeout(fetchHandle.current);
    };
  }, []);

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
    return closest;
  }, [topLevelAreas, center, zoom]);

  const subareas = useMemo(
    () => (activeTopLevelArea ? areas.filter((a) => a.parentAreaId === activeTopLevelArea.id) : []),
    [areas, activeTopLevelArea],
  );

  return (
    <div className="area-map-wrap">
      <MapContainer center={SPAIN_CENTER} zoom={SPAIN_ZOOM} maxZoom={17} className="area-map">
        <TileLayer
          attribution='Datos: &copy; colaboradores de <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>, SRTM | Mapa: &copy; <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)'
          url="https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png"
        />
        <BoundsTracker onChange={handleBoundsChange} />

        {topLevelAreas.map((area) => {
          const centroid = centroidOf(area);
          if (!centroid) return null;
          return (
            <Marker key={area.id} position={[centroid.lat, centroid.lng]}>
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

        {activeTopLevelArea && activeTopLevelArea.polygon.length > 0 && (
          <Polygon
            positions={activeTopLevelArea.polygon.map((p): [number, number] => [p.lat, p.lng])}
            pathOptions={{ color: '#3182ce' }}
          />
        )}

        {subareas.map((area) =>
          area.polygon.length > 0 ? (
            <Polygon
              key={area.id}
              positions={area.polygon.map((p): [number, number] => [p.lat, p.lng])}
              pathOptions={{ color: '#38a169' }}
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
    </div>
  );
}
