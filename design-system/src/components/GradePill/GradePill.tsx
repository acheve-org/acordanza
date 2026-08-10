import { gradeBand } from './gradeBand'
import './GradePill.css'

export interface GradePillProps {
  children: string
}

export function GradePill({ children }: GradePillProps) {
  const band = gradeBand(children)
  const className = ['grade-pill', band && band !== '1-5' ? `grade-band-${band}` : undefined]
    .filter(Boolean)
    .join(' ')

  return <span className={className}>{children}</span>
}
