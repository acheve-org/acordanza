import { Chip, GradePill } from 'acordanza-design-system';
import type { RouteSummary } from '../api/types';
import { gearLabels, validationStatusLabels } from '../api/types';

interface Props {
  route: RouteSummary;
  onClick: () => void;
}

export function RouteCard({ route, onClick }: Props) {
  return (
    <div className="route-card" onClick={onClick}>
      <div className="rc-top">
        <GradePill>{route.grade}</GradePill>
        <div className="rc-icons">
          {route.hasTopos && (
            <svg className="rc-topo-icon" viewBox="0 0 24 24" fill="currentColor" aria-label="Tiene topo">
              <title>Tiene topo</title>
              <path d="M9 4l-1.5 2H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-3.5L15 4H9zm3 5a4.5 4.5 0 1 1 0 9 4.5 4.5 0 0 1 0-9z" />
            </svg>
          )}
          {route.hasTick && (
            <svg className="rc-tick-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-label="Ya la has registrado">
              <title>Ya la has registrado</title>
              <path d="M5 13l4 4L19 7" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          )}
        </div>
      </div>
      <div className="rc-main">
        <div className="rc-name">{route.name}</div>
        <div className="rc-sub">{route.areaPath}</div>
      </div>
      {route.validationStatus !== 'approved' && (
        <Chip status={route.validationStatus}>{validationStatusLabels[route.validationStatus]}</Chip>
      )}
      {route.gear && <span className="rc-type">{gearLabels[route.gear] ?? route.gear}</span>}
    </div>
  );
}
