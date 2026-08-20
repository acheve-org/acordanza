import { useCallback, useEffect, useRef, useState } from 'react';
import * as areasApi from '../api/areas';
import type { AreaBounds, AreaSummary } from '../api/types';

const BOUNDS_FETCH_DEBOUNCE_MS = 300;

/**
 * Debounces bounds updates (map pan/zoom events, not a state value — see
 * frontend/CLAUDE.md's data-fetching bullet on why this stays off useDebouncedValue)
 * and fetches the areas within them, guarding against a stale response landing after
 * a newer request has already been sent.
 */
export function useViewportAreas() {
  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const fetchHandle = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Bumped on every fetch actually sent, so a slow/out-of-order response can
  // recognize it's stale (a newer fetch has since started) and skip setAreas.
  const requestId = useRef(0);

  const setBounds = useCallback((bounds: AreaBounds) => {
    if (fetchHandle.current) clearTimeout(fetchHandle.current);
    fetchHandle.current = setTimeout(() => {
      const thisRequestId = ++requestId.current;
      areasApi.listAreas(bounds).then((fetched) => {
        if (thisRequestId === requestId.current) setAreas(fetched);
      });
    }, BOUNDS_FETCH_DEBOUNCE_MS);
  }, []);

  useEffect(() => {
    return () => {
      if (fetchHandle.current) clearTimeout(fetchHandle.current);
    };
  }, []);

  return { areas, setBounds };
}
