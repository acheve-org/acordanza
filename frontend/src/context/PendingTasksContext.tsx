import { createContext, useContext, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import * as routesApi from '../api/routes';
import * as areasApi from '../api/areas';
import * as areaExpertsApi from '../api/areaExperts';
import * as firstAscensionistsApi from '../api/firstAscensionists';
import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import { queryKeys } from '../api/queryKeys';
import type {
  AreaExpert,
  AreaPending,
  FirstAscensionistLinkRequestSummary,
  FirstAscensionistPending,
  RoutePending,
} from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from './AuthContext';

interface PendingTasksValue {
  pendingRoutes: RoutePending[];
  pendingAreas: AreaPending[];
  pendingAreaExperts: AreaExpert[];
  pendingFirstAscensionistLinks: FirstAscensionistLinkRequestSummary[];
  pendingFirstAscensionists: FirstAscensionistPending[];
  total: number;
  visible: boolean;
  showRoutes: boolean;
  showAreas: boolean;
  showAreaExperts: boolean;
  showFirstAscensionistLinks: boolean;
  showFirstAscensionists: boolean;
  loading: boolean;
}

const PendingTasksContext = createContext<PendingTasksValue | null>(null);

export function PendingTasksProvider({ children }: { children: ReactNode }) {
  const { hasPermission, isAreaExpert } = useAuth();

  const showRoutes = hasPermission(Permissions.routeValidate) || isAreaExpert;
  const showAreas = hasPermission(Permissions.areaValidate);
  const showAreaExperts = hasPermission(Permissions.areaExpertApprove);
  const showFirstAscensionistLinks = hasPermission(Permissions.firstAscensionistLinkApprove);
  const showFirstAscensionists = hasPermission(Permissions.firstAscensionistValidate);
  const visible = showRoutes || showAreas || showAreaExperts || showFirstAscensionistLinks || showFirstAscensionists;

  const routesQuery = useQuery({
    queryKey: queryKeys.pendingRoutes(),
    queryFn: () => routesApi.listPendingRoutes(),
    enabled: showRoutes,
  });
  const areasQuery = useQuery({
    queryKey: queryKeys.pendingAreas(),
    queryFn: () => areasApi.listPendingAreas(),
    enabled: showAreas,
  });
  const areaExpertsQuery = useQuery({
    queryKey: queryKeys.pendingAreaExperts(),
    queryFn: () => areaExpertsApi.listPendingAreaExperts(),
    enabled: showAreaExperts,
  });
  const firstAscensionistLinksQuery = useQuery({
    queryKey: queryKeys.pendingFirstAscensionistLinks(),
    queryFn: () => firstAscensionistLinksApi.listPendingFirstAscensionistLinks(),
    enabled: showFirstAscensionistLinks,
  });
  const firstAscensionistsQuery = useQuery({
    queryKey: queryKeys.pendingFirstAscensionists(),
    queryFn: () => firstAscensionistsApi.listPendingFirstAscensionists(),
    enabled: showFirstAscensionists,
  });

  const pendingRoutes = routesQuery.data ?? [];
  const pendingAreas = areasQuery.data ?? [];
  const pendingAreaExperts = areaExpertsQuery.data ?? [];
  const pendingFirstAscensionistLinks = firstAscensionistLinksQuery.data ?? [];
  const pendingFirstAscensionists = firstAscensionistsQuery.data ?? [];

  const loading = [routesQuery, areasQuery, areaExpertsQuery, firstAscensionistLinksQuery, firstAscensionistsQuery].some(
    (q) => q.isLoading,
  );

  const total =
    pendingRoutes.length +
    pendingAreas.length +
    pendingAreaExperts.length +
    pendingFirstAscensionistLinks.length +
    pendingFirstAscensionists.length;

  return (
    <PendingTasksContext.Provider
      value={{
        pendingRoutes,
        pendingAreas,
        pendingAreaExperts,
        pendingFirstAscensionistLinks,
        pendingFirstAscensionists,
        total,
        visible,
        showRoutes,
        showAreas,
        showAreaExperts,
        showFirstAscensionistLinks,
        showFirstAscensionists,
        loading,
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
