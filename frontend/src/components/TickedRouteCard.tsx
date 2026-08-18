import { Fragment, useState } from 'react';
import { ConfirmModal, GradePill, RemoveButton, gradeBand } from 'acordanza-design-system';
import type { TickedRoute } from '../api/types';
import { RouteMeta } from './RouteMeta';

interface Props {
  tick: TickedRoute;
  onSelect: () => void;
  onDelete?: () => void;
}

export function TickedRouteCard({ tick, onSelect, onDelete }: Props) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const band = gradeBand(tick.grade);
  const bandClass = band && band !== '1-5' ? ` grade-band-${band}` : '';

  return (
    <Fragment>
      <div className={`route-card${bandClass}`} onClick={onSelect}>
        <div className="rc-top">
          <GradePill grade={tick.grade} aidGrade={tick.aidGrade} />
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
        <RouteMeta orientation={tick.orientation} length={tick.length} gear={tick.gear} />
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
