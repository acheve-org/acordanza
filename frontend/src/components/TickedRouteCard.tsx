import { GradePill } from 'acordanza-design-system';
import type { TickedRoute } from '../api/types';
import { gearLabels } from '../api/types';

interface Props {
  tick: TickedRoute;
  onSelect: () => void;
  onDelete?: () => void;
}

export function TickedRouteCard({ tick, onSelect, onDelete }: Props) {
  return (
    <div className="route-card" onClick={onSelect}>
      <GradePill>{tick.grade}</GradePill>
      <div className="rc-main">
        <div className="rc-name">{tick.routeName}</div>
        <div className="rc-sub">{tick.areaPath}</div>
      </div>
      {tick.gear && <span className="rc-type">{gearLabels[tick.gear] ?? tick.gear}</span>}
      <div className="trc-tick">
        <span className="trc-date">
          {tick.date}
          {tick.rating ? ` · ${'★'.repeat(tick.rating)}` : ''}
        </span>
        {tick.notes && <span className="trc-notes">{tick.notes}</span>}
      </div>
      {onDelete && (
        <button
          className="tick-remove"
          title="Eliminar registro"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
        >
          &times;
        </button>
      )}
    </div>
  );
}
