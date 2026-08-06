import type { ReactNode } from 'react'
import { Button } from '../Button/Button'
import { Modal } from './Modal'
import { ModalActions } from './ModalActions'

export interface ConfirmModalProps {
  message: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  /** Red confirm button, for destructive actions (deleting something). */
  danger?: boolean
  onConfirm: () => void
  onClose: () => void
}

export function ConfirmModal({
  message,
  confirmLabel = 'Confirmar',
  cancelLabel = 'Cancelar',
  danger = false,
  onConfirm,
  onClose,
}: ConfirmModalProps) {
  function handleConfirm() {
    onConfirm()
    onClose()
  }

  return (
    <Modal onClose={onClose}>
      <p>{message}</p>
      <ModalActions>
        <Button variant="ghost" type="button" onClick={onClose}>
          {cancelLabel}
        </Button>
        <Button variant="ghost" danger={danger} type="button" onClick={handleConfirm}>
          {confirmLabel}
        </Button>
      </ModalActions>
    </Modal>
  )
}
