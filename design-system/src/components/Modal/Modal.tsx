import { useEffect, useRef, type MouseEvent, type ReactNode } from 'react'
import './Modal.css'

export interface ModalProps {
  onClose: () => void
  /** Wider max-width, for content like an embedded map that needs more room. */
  wide?: boolean
  children: ReactNode
}

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

// Every mounted Modal registers itself here so that when modals stack (e.g. a
// confirmation on top of a detail modal), only the topmost one reacts to
// Escape/Tab — otherwise every instance's document-level keydown listener
// would fire on the same keypress, closing all of them at once.
let modalStack: symbol[] = []

export function Modal({ onClose, wide = false, children }: ModalProps) {
  const modalRef = useRef<HTMLDivElement>(null)
  const idRef = useRef(Symbol())

  // Escape-to-close, a focus trap so Tab can't reach obscured page content,
  // and moving focus into the dialog on open / back to the trigger on close
  // — none of this comes for free from a plain <div>, unlike a native <dialog>.
  useEffect(() => {
    const modal = modalRef.current
    const id = idRef.current
    modalStack.push(id)
    const previouslyFocused = document.activeElement as HTMLElement | null
    const initial = modal?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR)
    ;(initial ?? modal)?.focus()

    function handleKeyDown(e: KeyboardEvent) {
      if (modalStack[modalStack.length - 1] !== id) return
      if (e.key === 'Escape') {
        onClose()
        return
      }
      if (e.key !== 'Tab' || !modal) return
      const items = modal.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)
      if (items.length === 0) return
      const first = items[0]
      const last = items[items.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      modalStack = modalStack.filter((m) => m !== id)
      previouslyFocused?.focus()
    }
  }, [onClose])

  function handleOverlayClick(e: MouseEvent<HTMLDivElement>) {
    if (e.target === e.currentTarget) onClose()
  }

  return (
    <div className="modal-overlay" onClick={handleOverlayClick}>
      <div ref={modalRef} className={wide ? 'modal modal-wide' : 'modal'} role="dialog" aria-modal="true" tabIndex={-1}>
        {children}
      </div>
    </div>
  )
}
