import type { RouteSummary } from '../api/types';
import { gearLabels } from '../api/types';

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
        <div className="rc-sub">{route.cragPath}</div>
      </div>
      {route.gear && <span className="rc-type">{gearLabels[route.gear] ?? route.gear}</span>}
      <span className="rc-ticks">
        {route.tickCount} registro{route.tickCount === 1 ? '' : 's'}
      </span>
    </div>
  );
}
