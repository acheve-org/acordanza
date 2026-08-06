import { Button, List, ListRow, RemoveButton } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
}

export function Default() {
  return (
    <div style={canvas}>
      <List>
        <ListRow
          title="Marta Ruiz"
          subtitle="Solicitud pendiente"
          trailing={<RemoveButton label="Retirar solicitud" onClick={() => {}} />}
        />
        <ListRow
          title="Jon Etxeberria"
          trailing={
            <Button variant="ghost" small type="button" onClick={() => {}}>
              Solicitar
            </Button>
          }
        />
        <ListRow title="Fisura del Roto" subtitle="Roca Roja · 6a+" onClick={() => {}} />
      </List>
    </div>
  )
}
