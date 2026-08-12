import { CompassRose, orientationAbbr, type Orientation } from 'acordanza-design-system';
import { gearLabels } from '../api/types';

interface Props {
  orientation: string | null;
  length: number;
  gear: string | null;
}

export function RouteMeta({ orientation, length, gear }: Props) {
  const text = [
    orientation && orientationAbbr[orientation as Orientation],
    length ? `${length} m` : null,
    gear && gearLabels[gear],
  ]
    .filter(Boolean)
    .join(' · ');

  if (!text) return null;

  return (
    <div className="rc-meta">
      {orientation && <CompassRose size="sm" value={orientation as Orientation} />}
      {text}
    </div>
  );
}
