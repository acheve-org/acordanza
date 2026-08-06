import { Fragment, useState } from 'react';
import { ConfirmModal, GradePill } from 'acordanza-design-system';
import type { TickedRoute } from '../api/types';
import { gearLabels } from '../api/types';

interface Props {
  tick: TickedRoute;
  onSelect: () => void;
  onDelete?: () => void;
}

export function TickedRouteCard({ tick, onSelect, onDelete }: Props) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  return (
    <Fragment>
      <div className="route-card" onClick={onSelect}>
        <div className="rc-top">
          <GradePill>{tick.grade}</GradePill>
          {onDelete && (
            <button
              className="tick-remove"
              title="Eliminar registro"
              onClick={(e) => {
                e.stopPropagation();
                setConfirmingDelete(true);
              }}
            >
              &times;
            </button>
          )}
        </div>
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
      </div>

      {confirmingDelete && onDelete && (
        <ConfirmModal
          message="¿Eliminar este registro de ascenso?"
          confirmLabel="Eliminar"
          danger
          onConfirm={onDelete}
          onClose={() => setConfirmingDelete(false)}
        />
      )}
    </Fragment>
  );
}
