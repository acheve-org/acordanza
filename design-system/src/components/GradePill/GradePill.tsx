import { gradeBand } from './gradeBand'
import './GradePill.css'

export interface GradePillProps {
  grade: string
  obligatoryGrade?: string | null
}

export function GradePill({ grade, obligatoryGrade }: GradePillProps) {
  const band = gradeBand(grade)
  const className = ['grade-pill', band && band !== '1-5' ? `grade-band-${band}` : undefined]
    .filter(Boolean)
    .join(' ')
  const text = obligatoryGrade ? `${grade} (${obligatoryGrade})` : grade

  return <span className={className}>{text}</span>
}
