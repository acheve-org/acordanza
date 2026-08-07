import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import * as areaExpertsApi from '../api/areaExperts';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import type { AreaExpert, AreaSummary, FirstAscensionistLinkRequestSummary, RouteSummary } from '../api/types';
import { emptyRouteFilters } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from './AuthContext';

interface PendingTasksValue {
  pendingRoutes: RouteSummary[];
  pendingAreas: AreaSummary[];
  // All areas (validated or not), keyed by id — lets callers resolve a pending area's
  // ancestor chain even when some ancestors are already validated and so absent from
  // `pendingAreas` itself.
  areasById: Map<number, AreaSummary>;
  pendingAreaExperts: AreaExpert[];
  pendingFirstAscensionistLinks: FirstAscensionistLinkRequestSummary[];
  total: number;
  visible: boolean;
  showRoutes: boolean;
  showAreas: boolean;
  showAreaExperts: boolean;
  showFirstAscensionistLinks: boolean;
  loading: boolean;
  refresh: () => void;
}

const PendingTasksContext = createContext<PendingTasksValue | null>(null);

export function PendingTasksProvider({ children }: { children: ReactNode }) {
  const { username, hasPermission, isAreaExpert } = useAuth();

  const showRoutes = hasPermission(Permissions.routeValidate) || isAreaExpert;
  const showAreas = hasPermission(Permissions.areaValidate);
  const showAreaExperts = hasPermission(Permissions.areaExpertApprove);
  const showFirstAscensionistLinks = hasPermission(Permissions.firstAscensionistLinkApprove);
  const visible = showRoutes || showAreas || showAreaExperts || showFirstAscensionistLinks;

  const [pendingRoutes, setPendingRoutes] = useState<RouteSummary[]>([]);
  const [pendingAreas, setPendingAreas] = useState<AreaSummary[]>([]);
  const [allAreas, setAllAreas] = useState<AreaSummary[]>([]);
  const [pendingAreaExperts, setPendingAreaExperts] = useState<AreaExpert[]>([]);
  const [pendingFirstAscensionistLinks, setPendingFirstAscensionistLinks] = useState<
    FirstAscensionistLinkRequestSummary[]
  >([]);
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(() => {
    if (!username || !visible) {
      setPendingRoutes([]);
      setPendingAreas([]);
      setAllAreas([]);
      setPendingAreaExperts([]);
      setPendingFirstAscensionistLinks([]);
      return;
    }

    setLoading(true);
    // allSettled, not all: one category failing (e.g. a transient network error on
    // /area-experts/pending) shouldn't wipe out the others' already-fetched results,
    // and a rejection reaching Promise.all unhandled would otherwise surface as an
    // uncaught rejection instead of just leaving that category's count stale.
    Promise.allSettled([
      showRoutes
        ? routesApi.listRoutes(emptyRouteFilters).then((routes) => routes.filter((r) => !r.validatedAt && r.canValidate))
        : Promise.resolve<RouteSummary[]>([]),
      showAreas ? areasApi.listAreas() : Promise.resolve<AreaSummary[]>([]),
      showAreaExperts ? areaExpertsApi.listPendingAreaExperts() : Promise.resolve<AreaExpert[]>([]),
      showFirstAscensionistLinks
        ? firstAscensionistLinksApi.listPendingFirstAscensionistLinks()
        : Promise.resolve<FirstAscensionistLinkRequestSummary[]>([]),
    ])
      .then(([routes, areas, areaExperts, firstAscensionistLinks]) => {
        if (routes.status === 'fulfilled') setPendingRoutes(routes.value);
        else console.error('No se pudieron cargar las vías pendientes', routes.reason);
        if (areas.status === 'fulfilled') {
          setAllAreas(areas.value);
          setPendingAreas(areas.value.filter((a) => !a.validatedAt));
        } else console.error('No se pudieron cargar los sectores pendientes', areas.reason);
        if (areaExperts.status === 'fulfilled') setPendingAreaExperts(areaExperts.value);
        else console.error('No se pudieron cargar las solicitudes de expertos de sector', areaExperts.reason);
        if (firstAscensionistLinks.status === 'fulfilled') setPendingFirstAscensionistLinks(firstAscensionistLinks.value);
        else console.error('No se pudieron cargar las solicitudes de vínculo de aperturista', firstAscensionistLinks.reason);
      })
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [username, visible, showRoutes, showAreas, showAreaExperts, showFirstAscensionistLinks]);

  useEffect(refresh, [refresh]);

  const areasById = useMemo(() => new Map(allAreas.map((a) => [a.id, a])), [allAreas]);

  const total =
    pendingRoutes.length + pendingAreas.length + pendingAreaExperts.length + pendingFirstAscensionistLinks.length;

  return (
    <PendingTasksContext.Provider
      value={{
        pendingRoutes,
        pendingAreas,
        areasById,
        pendingAreaExperts,
        pendingFirstAscensionistLinks,
        total,
        visible,
        showRoutes,
        showAreas,
        showAreaExperts,
        showFirstAscensionistLinks,
        loading,
        refresh,
      }}
    >
      {children}
    </PendingTasksContext.Provider>
  );
}

export function usePendingTasks() {
  const ctx = useContext(PendingTasksContext);
  if (!ctx) throw new Error('usePendingTasks must be used within PendingTasksProvider');
  return ctx;
}
