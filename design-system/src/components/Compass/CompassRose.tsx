import { needlePath, orientationLabels, orientations, type Orientation } from './compass'
import './Compass.css'

export interface CompassRoseProps {
  value: Orientation
  size?: 'sm' | 'md'
}

export function CompassRose({ value, size = 'md' }: CompassRoseProps) {
  const index = orientations.indexOf(value)
  const label = `Orientación: ${orientationLabels[value]}`

  return (
    <svg className={`compass-rose compass-rose-${size}`} viewBox="0 0 100 100" role="img" aria-label={label}>
      <title>{label}</title>
      <circle className="compass-rose-ring" cx={50} cy={50} r={40} />
      <line className="compass-rose-north" x1={50} y1={0} x2={50} y2={6} />
      <path className="compass-rose-needle" d={needlePath(index, 50, 50, 35)} />
    </svg>
  )
}
