import { Chip } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
  display: 'inline-flex',
}

export function Default() {
  return (
    <div style={canvas}>
      <Chip>Pendiente</Chip>
    </div>
  )
}

export function Removable() {
  return (
    <div style={canvas}>
      <Chip onRemove={() => {}}>regletas</Chip>
    </div>
  )
}

export function TagRow() {
  return (
    <div style={{ ...canvas, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
      <Chip onRemove={() => {}}>regletas</Chip>
      <Chip onRemove={() => {}}>clásica</Chip>
      <Chip onRemove={() => {}}>desplome</Chip>
    </div>
  )
}

export function ValidationStatus() {
  return (
    <div style={{ ...canvas, display: 'flex', gap: 6 }}>
      <Chip status="pending">Pendiente de validación</Chip>
      <Chip status="approved">Validado</Chip>
      <Chip status="rejected">Rechazado</Chip>
    </div>
  )
}
