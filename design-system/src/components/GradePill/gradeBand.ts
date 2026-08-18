export type GradeBand = '1-5' | '6' | '7' | '8-9'

export function gradeBand(grade: string | null | undefined): GradeBand | null {
  const g = (grade ?? '').trim().toLowerCase()
  if (!/^[1-9][a-c]\+?$/.test(g)) return null
  const n = Number(g[0])
  if (n <= 5) return '1-5'
  if (n >= 8) return '8-9'
  return String(n) as GradeBand
}
