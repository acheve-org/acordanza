import type { ReactNode } from 'react'
import './Chip.css'

export interface ChipProps {
  children: ReactNode
  /** When provided, the chip renders a remove (×) button that calls this. */
  onRemove?: () => void
}

export function Chip({ children, onRemove }: ChipProps) {
  if (!onRemove) {
    return <span className="tag-chip">{children}</span>
  }

  return (
    <span className="tag-chip chip-removable">
      {children}
      <button type="button" className="chip-remove" onClick={onRemove}>
        &times;
      </button>
    </span>
  )
}
