import { Badge } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
}

const navItem: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 8,
  fontSize: 14,
}

export function Default() {
  return (
    <div style={canvas}>
      <span style={navItem}>
        Tareas pendientes <Badge count={4} />
      </span>
    </div>
  )
}

export function OverflowCount() {
  return (
    <div style={canvas}>
      <span style={navItem}>
        Solicitudes <Badge count={128} />
      </span>
    </div>
  )
}
