import { createContext } from 'react';
import type {
  AreaExpert,
  AreaPending,
  FirstAscensionistLinkRequestSummary,
  FirstAscensionistPending,
  RoutePending,
} from '../api/types';

export interface PendingTasksValue {
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

export const PendingTasksContext = createContext<PendingTasksValue | null>(null);
