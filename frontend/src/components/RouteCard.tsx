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
        <div className="rc-sub">{route.areaPath}</div>
      </div>
      {route.gear && <span className="rc-type">{gearLabels[route.gear] ?? route.gear}</span>}
      {route.hasTopos && (
        <svg className="rc-topo-icon" viewBox="0 0 24 24" fill="currentColor" aria-label="Tiene topo">
          <title>Tiene topo</title>
          <path d="M9 4l-1.5 2H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-3.5L15 4H9zm3 5a4.5 4.5 0 1 1 0 9 4.5 4.5 0 0 1 0-9z" />
        </svg>
      )}
      <span className="rc-ticks">
        {route.tickCount} registro{route.tickCount === 1 ? '' : 's'}
      </span>
    </div>
  );
}
