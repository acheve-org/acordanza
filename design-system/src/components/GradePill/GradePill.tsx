import type { ReactNode } from 'react'
import './GradePill.css'

export interface GradePillProps {
  children: ReactNode
}

export function GradePill({ children }: GradePillProps) {
  return <span className="grade-pill">{children}</span>
}
