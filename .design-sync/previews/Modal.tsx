import { useState } from 'react'
import { Button, Modal, ModalActions } from 'acordanza-design-system'

export function AddTick() {
  const [open, setOpen] = useState(true)
  if (!open) return null
  return (
    <Modal onClose={() => setOpen(false)}>
      <h2>Registrar ascenso</h2>
      <p className="detail-meta">Fisura del Roto — 6a+</p>
      <form onSubmit={(e) => e.preventDefault()}>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
          <label>
            Fecha
            <input type="date" defaultValue="2026-07-12" readOnly />
          </label>
          <label>
            Puntuación (1–5)
            <input type="number" defaultValue={4} readOnly />
          </label>
        </div>
        <label>
          Notas
          <input type="text" defaultValue="Encadenada al segundo intento, el paso clave fue la regleta…" readOnly />
        </label>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={() => setOpen(false)}>
            Cancelar
          </Button>
          <Button type="submit">Registrar</Button>
        </ModalActions>
      </form>
    </Modal>
  )
}

export function Wide() {
  const [open, setOpen] = useState(true)
  if (!open) return null
  return (
    <Modal onClose={() => setOpen(false)} wide>
      <h2>Dibujar polígono del sector</h2>
      <p className="detail-meta">Sector: Roca Roja</p>
      <div
        style={{
          height: 220,
          borderRadius: 12,
          background: 'rgba(255,255,255,0.06)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--text-dim)',
        }}
      >
        Mapa del sector
      </div>
      <ModalActions>
        <Button variant="ghost" type="button" onClick={() => setOpen(false)}>
          Cancelar
        </Button>
        <Button type="button">Guardar polígono</Button>
      </ModalActions>
    </Modal>
  )
}
