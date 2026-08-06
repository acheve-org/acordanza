import { ListRow, RemoveButton } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
}

const listReset: React.CSSProperties = { listStyle: 'none', margin: 0, padding: 0 }

export function WithSubtitle() {
  return (
    <div style={canvas}>
      <ul style={listReset}>
        <ListRow title="Fisura del Roto" subtitle="Roca Roja · 6a+" onClick={() => {}} />
      </ul>
    </div>
  )
}

export function WithTrailingAction() {
  return (
    <div style={canvas}>
      <ul style={listReset}>
        <ListRow
          title="Marta Ruiz"
          subtitle="Solicitud pendiente"
          trailing={<RemoveButton label="Retirar solicitud" onClick={() => {}} />}
        />
      </ul>
    </div>
  )
}
