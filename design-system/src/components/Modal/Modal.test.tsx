import { render, fireEvent, screen } from '@testing-library/react'
import { Modal } from './Modal'

function renderModal(onClose = vi.fn()) {
  const utils = render(
    <Modal onClose={onClose}>
      <button>Primero</button>
      <button>Último</button>
    </Modal>,
  )
  return { onClose, ...utils }
}

describe('Modal', () => {
  it('moves focus to the first focusable element on open', () => {
    renderModal()
    expect(screen.getByRole('button', { name: 'Primero' })).toHaveFocus()
  })

  it('calls onClose on Escape', () => {
    const { onClose } = renderModal()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('calls onClose when the overlay (outside the dialog) is clicked', () => {
    const { onClose, container } = renderModal()
    const overlay = container.querySelector('.modal-overlay')!
    fireEvent.click(overlay)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('does not call onClose when clicking inside the dialog content', () => {
    const { onClose } = renderModal()
    fireEvent.click(screen.getByRole('dialog'))
    expect(onClose).not.toHaveBeenCalled()
  })

  it('traps Tab so it wraps from the last focusable element back to the first', () => {
    renderModal()
    const first = screen.getByRole('button', { name: 'Primero' })
    const last = screen.getByRole('button', { name: 'Último' })

    last.focus()
    fireEvent.keyDown(document, { key: 'Tab' })

    expect(first).toHaveFocus()
  })

  it('traps Shift+Tab so it wraps from the first focusable element back to the last', () => {
    renderModal()
    const first = screen.getByRole('button', { name: 'Primero' })
    const last = screen.getByRole('button', { name: 'Último' })

    first.focus()
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true })

    expect(last).toHaveFocus()
  })

  it('only reacts to the topmost modal when two are stacked', () => {
    // Mirrors the real app: a ConfirmModal mounts as a sibling *after* the modal behind it
    // (see DetailModal.tsx), not nested inside it — two separate mounts, in order, so each
    // registers on the shared modalStack in the order they actually opened.
    const outerClose = vi.fn()
    const innerClose = vi.fn()
    render(
      <Modal onClose={outerClose}>
        <button>Fuera</button>
      </Modal>,
    )
    render(
      <Modal onClose={innerClose}>
        <button>Dentro</button>
      </Modal>,
    )

    fireEvent.keyDown(document, { key: 'Escape' })

    expect(innerClose).toHaveBeenCalledTimes(1)
    expect(outerClose).not.toHaveBeenCalled()
  })
})
