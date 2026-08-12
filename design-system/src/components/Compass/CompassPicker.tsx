import { useId, type CSSProperties } from 'react'
import { orientationLabels, orientations, wedgePath, type Orientation } from './compass'
import './Compass.css'

type MultiProps = {
  multiple?: true
  value: Orientation[]
  onChange: (value: Orientation[]) => void
}

type SingleProps = {
  multiple: false
  value: Orientation | null
  onChange: (value: Orientation | null) => void
}

export type CompassPickerProps = MultiProps | SingleProps

// 3x3 grid position for each sector — centre cell stays empty. DOM order below still
// follows `orientations` (clockwise from north), so Tab walks the compass in that order
// regardless of this layout.
const GRID: Record<Orientation, { row: number; col: number }> = {
  northwest: { row: 0, col: 0 },
  north: { row: 0, col: 1 },
  northeast: { row: 0, col: 2 },
  west: { row: 1, col: 0 },
  east: { row: 1, col: 2 },
  southwest: { row: 2, col: 0 },
  south: { row: 2, col: 1 },
  southeast: { row: 2, col: 2 },
}

export function CompassPicker(props: CompassPickerProps) {
  const name = useId()
  const multiple = props.multiple !== false

  function isChecked(o: Orientation): boolean {
    return multiple ? props.value.includes(o) : props.value === o
  }

  function handleChange(o: Orientation, checked: boolean) {
    if (multiple) {
      const { value, onChange } = props
      const next = checked ? [...value, o] : value.filter((v) => v !== o)
      // Canonical compass order, always — so two differently-ordered selections
      // normalise to the same react-query key (see frontend's queryKeys.routes).
      onChange(orientations.filter((x) => next.includes(x)))
    } else {
      props.onChange(o)
    }
  }

  return (
    <div className="compass-picker">
      {orientations.map((o, i) => {
        const { row, col } = GRID[o]
        return (
          <label key={o} className="compass-cell" style={{ '--r': row, '--c': col } as CSSProperties}>
            <input
              type={multiple ? 'checkbox' : 'radio'}
              name={name}
              value={o}
              checked={isChecked(o)}
              aria-label={orientationLabels[o]}
              onChange={(e) => handleChange(o, e.target.checked)}
            />
            <svg viewBox="0 0 100 100" aria-hidden="true">
              <path className="compass-wedge" d={wedgePath(i, 50, 50, 46, 22)} />
            </svg>
          </label>
        )
      })}
    </div>
  )
}
