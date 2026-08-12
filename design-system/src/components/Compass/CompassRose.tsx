import { needlePath, orientationLabels, orientations, wedgePath, type Orientation } from './compass'
import './Compass.css'

export interface CompassRoseProps {
  value: Orientation
  size?: 'sm' | 'md'
}

const CARDINAL_LETTERS: Partial<Record<Orientation, string>> = {
  north: 'N',
  east: 'E',
  south: 'S',
  west: 'O',
}

export function CompassRose({ value, size = 'md' }: CompassRoseProps) {
  const index = orientations.indexOf(value)
  const label = `Orientación: ${orientationLabels[value]}`

  return (
    <svg
      className={`compass-rose compass-rose-${size}`}
      viewBox="0 0 100 100"
      role="img"
      aria-label={label}
    >
      <title>{label}</title>
      {size === 'sm' ? (
        <>
          <circle className="compass-rose-ring" cx={50} cy={50} r={43} />
          <path className="compass-rose-needle" d={needlePath(index, 50, 50, 38)} />
        </>
      ) : (
        <>
          {orientations.map((o, i) => (
            <path
              key={o}
              className={`compass-wedge${o === value ? ' compass-wedge-selected' : ''}`}
              d={wedgePath(i, 50, 50, 46, 22)}
            />
          ))}
          {orientations.map((o, i) => {
            const letter = CARDINAL_LETTERS[o]
            if (!letter) return null
            const mid = -90 + 45 * i
            const rad = (mid * Math.PI) / 180
            return (
              <text
                key={o}
                className="compass-rose-letter"
                x={50 + 44 * Math.cos(rad)}
                y={50 + 44 * Math.sin(rad)}
                textAnchor="middle"
                dominantBaseline="central"
              >
                {letter}
              </text>
            )
          })}
        </>
      )}
    </svg>
  )
}
