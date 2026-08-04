import { GradePill } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
  display: 'inline-flex',
}

export function Sport() {
  return (
    <div style={canvas}>
      <GradePill>6a+</GradePill>
    </div>
  )
}

export function Trad() {
  return (
    <div style={canvas}>
      <GradePill>V0 5.9</GradePill>
    </div>
  )
}

export function Row() {
  return (
    <div style={{ ...canvas, display: 'flex', gap: 8 }}>
      <GradePill>5a</GradePill>
      <GradePill>6a+</GradePill>
      <GradePill>7b</GradePill>
      <GradePill>8a</GradePill>
    </div>
  )
}
