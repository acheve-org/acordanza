import type { MouseEvent, ReactNode } from 'react'
import './Modal.css'

export interface ModalProps {
  onClose: () => void
  /** Wider max-width, for content like an embedded map that needs more room. */
  wide?: boolean
  children: ReactNode
}

export function Modal({ onClose, wide = false, children }: ModalProps) {
  function handleOverlayClick(e: MouseEvent<HTMLDivElement>) {
    if (e.target === e.currentTarget) onClose()
  }

  return (
    <div className="modal-overlay" onClick={handleOverlayClick}>
      <div className={wide ? 'modal modal-wide' : 'modal'}>{children}</div>
    </div>
  )
}
