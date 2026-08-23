import { CompassRose, orientationAbbr, type Orientation } from 'acordanza-design-system';
import { gearLabels, type Gear } from '../api/types';

interface Props {
  orientation: string | null;
  length: number | null;
  gear: Gear | null;
  bottomAligned?: boolean;
}

export function RouteMeta({ orientation, length, gear, bottomAligned }: Props) {
  if (!orientation && !length && !gear) return null;

  return (
    <div className={bottomAligned ? 'rc-meta rc-meta-bottom' : 'rc-meta'}>
      {orientation && (
        <span className="rc-meta-item">
          <CompassRose size="sm" value={orientation as Orientation} />
          {orientationAbbr[orientation as Orientation]}
        </span>
      )}
      {length ? <span className="rc-meta-item">{`${length} m`}</span> : null}
      {gear && <span className="rc-meta-item">{gearLabels[gear]}</span>}
    </div>
  );
}
