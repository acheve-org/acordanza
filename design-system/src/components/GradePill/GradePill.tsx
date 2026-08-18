import { gradeBand } from './gradeBand'
import './GradePill.css'

export interface GradePillProps {
  grade: string | null
  aidGrade?: string | null
  obligatoryGrade?: string | null
}

// The band comes from the free grade only — the four bands mean French difficulty and
// nothing else, so an aid-only route keeps the default teal rather than inventing a fifth
// hue for a scale that carries no ordering the reader already knows.
export function GradePill({ grade, aidGrade, obligatoryGrade }: GradePillProps) {
  const band = grade ? gradeBand(grade) : null
  const className = [
    'grade-pill',
    band && band !== '1-5' ? `grade-band-${band}` : undefined,
    !grade && !aidGrade ? 'grade-pill-empty' : undefined,
  ]
    .filter(Boolean)
    .join(' ')

  const combined = [grade, aidGrade].filter(Boolean).join('/') || 'Sin grado'
  const text = obligatoryGrade ? `${combined} (${obligatoryGrade})` : combined

  return <span className={className}>{text}</span>
}
