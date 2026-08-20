import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import { EmptyState, List, ListRow } from 'acordanza-design-system';
import { useGeolocation } from '../hooks/useGeolocation';
import { useViewportAreas } from '../hooks/useViewportAreas';
import {
  deriveVisibleAreas,
  DEFAULT_LOCATION_ZOOM,
  SUBAREA_ZOOM,
  sortByDistance,
  boundsOfPolygon,
} from '../lib/exploreAreas';
import type { AreaBounds, AreaSummary } from '../api/types';
import type { FlyTarget } from '../components/AreaMap';

const AreaMap = lazy(() => import('../components/AreaMap').then((m) => ({ default: m.AreaMap })));

function RootIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      style={{ color: 'var(--depth-root)' }}
    >
      <path d="M3 19l6-10 4 6 3-5 5 9H3z" strokeLinejoin="round" />
    </svg>
  );
}

function SubareaIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      style={{ color: 'var(--depth-subarea)' }}
    >
      <path d="M12 21s7-6.5 7-12a7 7 0 1 0-14 0c0 5.5 7 12 7 12z" strokeLinejoin="round" />
      <circle cx="12" cy="9" r="2.5" />
    </svg>
  );
}

export function ExplorePage() {
  const { areas, setBounds } = useViewportAreas();
  const geo = useGeolocation();
  const [center, setCenter] = useState<{ lat: number; lng: number } | null>(null);
  const [zoom, setZoom] = useState(6);
  const [selectedAreaId, setSelectedAreaId] = useState<number | null>(null);
  const [flyTarget, setFlyTarget] = useState<FlyTarget | null>(null);
  const [mobileListOpen, setMobileListOpen] = useState(false);
  const rowRefs = useRef<Record<number, HTMLLIElement | null>>({});

  function handleBoundsChange(bounds: AreaBounds, mapZoom: number) {
    setCenter({ lat: (bounds.minLat + bounds.maxLat) / 2, lng: (bounds.minLng + bounds.maxLng) / 2 });
    setZoom(mapZoom);
    setBounds(bounds);
  }

  useEffect(() => {
    if (geo.position) {
      setFlyTarget({
        center: [geo.position.coords.latitude, geo.position.coords.longitude],
        zoom: DEFAULT_LOCATION_ZOOM,
      });
    }
  }, [geo.position]);

  useEffect(() => {
    if (selectedAreaId != null) rowRefs.current[selectedAreaId]?.scrollIntoView({ block: 'nearest' });
  }, [selectedAreaId]);

  const { topLevelAreas, activeTopLevelArea, subareas } = useMemo(
    () => deriveVisibleAreas(areas, center, zoom),
    [areas, center, zoom],
  );

  const visibleAreas = useMemo(
    () => sortByDistance([...topLevelAreas, ...subareas], center),
    [topLevelAreas, subareas, center],
  );

  function handleSelectArea(area: AreaSummary) {
    setSelectedAreaId(area.id);
    setFlyTarget(
      area.polygon.length > 0
        ? { bounds: boundsOfPolygon(area.polygon) }
        : area.centroidLat != null && area.centroidLng != null
          ? { center: [area.centroidLat, area.centroidLng], zoom: SUBAREA_ZOOM }
          : null,
    );
    setMobileListOpen(false);
  }

  return (
    <main className="explore-page">
      <div className={`explore-list ${mobileListOpen ? 'is-open' : ''}`}>
        <button
          type="button"
          className="explore-list-handle"
          onClick={() => setMobileListOpen((open) => !open)}
        >
          {visibleAreas.length === 0 ? 'Zonas' : `Zonas (${visibleAreas.length})`}
        </button>

        <h1>Explora</h1>
        <p className="hint">
          Encuentra zonas que no conoces en el mapa, y descubre los sectores que las componen. Solo se muestran en el
          mapa los sectores que tienen información geográfica cargada.
        </p>

        <div className="explore-list-scroll">
          {visibleAreas.length === 0 ? (
            <EmptyState>No hay zonas en esta vista — aleja el mapa o desplázate.</EmptyState>
          ) : (
            <List>
              {visibleAreas.map((area) => {
                const isTopLevel = area.parentAreaId === null;
                return (
                  <ListRow
                    key={area.id}
                    ref={(row) => {
                      rowRefs.current[area.id] = row;
                    }}
                    title={area.name}
                    subtitle={isTopLevel ? undefined : activeTopLevelArea?.name}
                    leading={isTopLevel ? <RootIcon /> : <SubareaIcon />}
                    selected={area.id === selectedAreaId}
                    onClick={() => handleSelectArea(area)}
                  />
                );
              })}
            </List>
          )}
        </div>
      </div>

      <div className="explore-map">
        <Suspense fallback={<div className="area-map-placeholder" />}>
          <AreaMap
            areas={areas}
            onBoundsChange={handleBoundsChange}
            selectedAreaId={selectedAreaId}
            onSelectArea={(id) => setSelectedAreaId(id)}
            flyTarget={flyTarget}
            onLocateMe={geo.request}
            locating={geo.status === 'pending'}
          />
        </Suspense>
      </div>
    </main>
  );
}
