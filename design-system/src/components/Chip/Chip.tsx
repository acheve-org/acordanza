import type { ReactNode } from 'react'
import { RemoveButton } from '../RemoveButton/RemoveButton'
import './Chip.css'

export type ChipStatus = 'pending' | 'approved' | 'rejected'

export interface ChipProps {
  children: ReactNode
  /** When provided, the chip renders a remove (×) button that calls this. */
  onRemove?: () => void
  /** Visual status: `pending` (default, neutral) · `approved` (green) · `rejected` (red). */
  status?: ChipStatus
}

export function Chip({ children, onRemove, status = 'pending' }: ChipProps) {
  const className = ['tag-chip', status !== 'pending' ? `chip-${status}` : undefined]
    .filter(Boolean)
    .join(' ')

  if (!onRemove) {
    return <span className={className}>{children}</span>
  }

  return (
    <span className={`${className} chip-removable`}>
      {children}
      <RemoveButton label="Quitar" onClick={onRemove} />
    </span>
  )
}
