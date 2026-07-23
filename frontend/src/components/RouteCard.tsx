import type { RouteSummary } from '../api/types';
import { routeTypeLabels } from '../api/types';

interface Props {
  route: RouteSummary;
  onClick: () => void;
}

export function RouteCard({ route, onClick }: Props) {
  return (
    <div className="route-card" onClick={onClick}>
      <span className="grade-pill">{route.grade}</span>
      <div className="rc-main">
        <div className="rc-name">{route.name}</div>
        <div className="rc-sub">{route.crag}</div>
      </div>
      <span className="rc-type">{routeTypeLabels[route.type] ?? route.type}</span>
      <span className="rc-ticks">
        {route.tickCount} registro{route.tickCount === 1 ? '' : 's'}
      </span>
    </div>
  );
}
