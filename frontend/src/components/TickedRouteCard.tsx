import { Fragment, useState } from 'react';
import { ConfirmModal, GradePill, RemoveButton, gradeBand } from 'acordanza-design-system';
import type { TickedRoute } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';

interface Props {
  tick: TickedRoute;
  onSelect: () => void;
  onDelete?: () => void;
}

export function TickedRouteCard({ tick, onSelect, onDelete }: Props) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const band = gradeBand(tick.grade);
  const bandClass = band && band !== '1-5' ? ` grade-band-${band}` : '';
  const meta = [
    tick.orientation && orientationLabels[tick.orientation],
    tick.length ? `${tick.length} m` : null,
    tick.gear && gearLabels[tick.gear],
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <Fragment>
      <div className={`route-card${bandClass}`} onClick={onSelect}>
        <div className="rc-top">
          <GradePill>{tick.grade}</GradePill>
          {onDelete && (
            <RemoveButton
              label="Eliminar registro"
              onClick={(e) => {
                e.stopPropagation();
                setConfirmingDelete(true);
              }}
            />
          )}
        </div>
        <div className="rc-main">
          <div className="rc-name">{tick.routeName}</div>
          <div className="rc-sub">{tick.areaPath}</div>
        </div>
        {meta && <div className="rc-meta">{meta}</div>}
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
