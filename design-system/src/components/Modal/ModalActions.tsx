import type { ReactNode } from 'react'

export interface ModalActionsProps {
  children: ReactNode
}

export function ModalActions({ children }: ModalActionsProps) {
  return <div className="modal-actions">{children}</div>
}
