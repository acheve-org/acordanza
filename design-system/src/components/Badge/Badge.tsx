import './Badge.css'

export interface BadgeProps {
  /** Renders nothing when 0 (or less) — callers don't need to guard on count > 0 themselves. */
  count: number
  className?: string
}

export function Badge({ count, className }: BadgeProps) {
  if (count <= 0) return null

  return <span className={['badge', className].filter(Boolean).join(' ')}>{count > 99 ? '99+' : count}</span>
}
