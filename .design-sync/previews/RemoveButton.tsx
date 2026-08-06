import { RemoveButton } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
  display: 'flex',
  gap: 24,
  alignItems: 'flex-start',
}

const row: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  padding: '10px 14px',
  background: 'var(--bg-input)',
  border: '1px solid var(--border)',
  borderRadius: 8,
  fontSize: 13,
  width: 220,
}

const thumb: React.CSSProperties = {
  position: 'relative',
  width: 160,
  height: 120,
  borderRadius: 8,
  background: 'linear-gradient(135deg, var(--bg-input), var(--border))',
}

export function Plain() {
  return (
    <div style={canvas}>
      <div style={row}>
        <span>llave-portatil-2026</span>
        <RemoveButton label="Eliminar llave de acceso" onClick={() => {}} />
      </div>
    </div>
  )
}

export function Overlay() {
  return (
    <div style={canvas}>
      <div style={thumb}>
        <RemoveButton variant="overlay" label="Eliminar topo" onClick={() => {}} />
      </div>
    </div>
  )
}
