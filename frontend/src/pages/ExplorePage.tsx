
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
          No aparecen sectores sin ubicación.
        </p>

        <div className="explore-list-scroll">
          {visibleAreas.length === 0 ? (
            <EmptyState>No hay zonas en esta vista — aleja el mapa o desplázate.</EmptyState>
          ) : (
            <List>
              {visibleAreas.map((area) => {
                const isTopLevel = area.parentAreaId === null;
                const subtitle = isTopLevel
                  ? areas.some((a) => a.parentAreaId === area.id)
                    ? 'Con sectores'
                    : 'Sin sectores'
                  : activeTopLevelArea?.name;
                return (
                  <ListRow
                    key={area.id}
                    ref={(row) => {
                      rowRefs.current[area.id] = row;
                    }}
                    title={area.name}
                    subtitle={subtitle}
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
