import { render, fireEvent, screen } from '@testing-library/react'
import { ConfirmModal } from './ConfirmModal'

describe('ConfirmModal', () => {
  it('renders the message and default button labels', () => {
    render(<ConfirmModal message="¿Seguro?" onConfirm={() => {}} onClose={() => {}} />)

    expect(screen.getByText('¿Seguro?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeInTheDocument()
  })

  it('calls only onClose when cancelling', () => {
    const onConfirm = vi.fn()
    const onClose = vi.fn()
    render(<ConfirmModal message="¿Seguro?" onConfirm={onConfirm} onClose={onClose} />)

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(onConfirm).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('calls onConfirm then onClose when confirming', () => {
    const calls: string[] = []
    const onConfirm = vi.fn(() => calls.push('confirm'))
    const onClose = vi.fn(() => calls.push('close'))
    render(<ConfirmModal message="¿Seguro?" onConfirm={onConfirm} onClose={onClose} />)

    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }))

    expect(calls).toEqual(['confirm', 'close'])
  })

  it('supports custom labels', () => {
    render(
      <ConfirmModal
        message="¿Eliminar esta vía?"
        confirmLabel="Eliminar"
        cancelLabel="Volver"
        danger
        onConfirm={() => {}}
        onClose={() => {}}
      />,
    )

    expect(screen.getByRole('button', { name: 'Eliminar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Volver' })).toBeInTheDocument()
  })
})
