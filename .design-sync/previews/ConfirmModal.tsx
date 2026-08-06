import { useState } from 'react'
import { ConfirmModal } from 'acordanza-design-system'

export function DeleteRoute() {
  const [open, setOpen] = useState(true)
  if (!open) return null
  return (
    <ConfirmModal
      message='¿Eliminar esta vía y todos sus ascensos registrados?'
      confirmLabel="Eliminar"
      danger
      onConfirm={() => setOpen(false)}
      onClose={() => setOpen(false)}
    />
  )
}
