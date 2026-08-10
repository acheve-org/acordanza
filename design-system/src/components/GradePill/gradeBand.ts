/** The four colour bands of the French scale (issue #152). */
export type GradeBand = '1-5' | '6' | '7' | '8-9'

/**
 * The band a French-scale grade falls in, or `null` if it isn't one — which
 * doubles as the client-side validity check (the authority is the backend's
 * GradeValidator; this mirrors it).
 */
export function gradeBand(grade: string): GradeBand | null {
  const g = grade.trim().toLowerCase()
  if (!/^[1-9][a-c]\+?$/.test(g)) return null
  const n = Number(g[0])
  if (n <= 5) return '1-5'
  if (n >= 8) return '8-9'
  return String(n) as GradeBand
}
