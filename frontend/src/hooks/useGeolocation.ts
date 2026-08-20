import { useCallback, useEffect, useState } from 'react';

export type GeolocationStatus = 'idle' | 'pending' | 'granted' | 'denied' | 'unsupported';

/** A one-time "where am I browsing from" snapshot, not live tracking — no watchPosition. */
export function useGeolocation() {
  const [status, setStatus] = useState<GeolocationStatus>('idle');
  const [position, setPosition] = useState<GeolocationPosition | null>(null);

  const request = useCallback(() => {
    if (!('geolocation' in navigator)) {
      setStatus('unsupported');
      return;
    }
    setStatus('pending');
    navigator.geolocation.getCurrentPosition(
      (p) => {
        setPosition(p);
        setStatus('granted');
      },
      () => setStatus('denied'),
    );
  }, []);

  useEffect(() => {
    request();
    // Auto-prompt once on mount; `request` is stable (useCallback, no deps).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { status, position, request };
}
