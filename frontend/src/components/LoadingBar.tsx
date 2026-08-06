import { useSyncExternalStore } from 'react';
import { getApiActivitySnapshot, subscribeToApiActivity } from '../api/client';

// Global indicator that at least one API request is in flight — the API can
// be scaled to 0 and take a while to wake up, so this gives visual feedback
// without every page having to manage its own loading state.
export function LoadingBar() {
  const active = useSyncExternalStore(subscribeToApiActivity, getApiActivitySnapshot);

  return <div className={`loading-bar${active ? ' loading-bar-active' : ''}`} aria-hidden="true" />;
}
