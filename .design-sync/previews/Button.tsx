import { Button } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
  display: 'inline-flex',
}

export function Primary() {
  return (
    <div style={canvas}>
      <Button type="button">Registrar</Button>
    </div>
  )
}

export function PrimaryDisabled() {
  return (
    <div style={canvas}>
      <Button type="button" disabled>
        Registrando…
      </Button>
    </div>
  )
}

export function Ghost() {
  return (
    <div style={canvas}>
      <Button variant="ghost" type="button">
        Cancelar
      </Button>
    </div>
  )
}

export function GhostSmallDanger() {
  return (
    <div style={canvas}>
      <Button variant="ghost" small danger type="button">
        Rechazar
      </Button>
    </div>
  )
}

export function FullWidth() {
  return (
    <div style={{ ...canvas, width: 260, display: 'block' }}>
      <Button fullWidth type="button">
        + Añadir vía
      </Button>
    </div>
  )
}

export function AsLink() {
  return (
    <div style={canvas}>
      <Button as="a" variant="ghost" small href="#">
        ← Volver
      </Button>
    </div>
  )
}
