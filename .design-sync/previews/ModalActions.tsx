import { Button, ModalActions } from 'acordanza-design-system'

export function Default() {
  return (
    <div style={{ background: 'var(--bg-card)', color: 'var(--text)', padding: 24, borderRadius: 12 }}>
      <ModalActions>
        <Button variant="ghost" type="button">
          Cancelar
        </Button>
        <Button type="submit">Registrar</Button>
      </ModalActions>
    </div>
  )
}
