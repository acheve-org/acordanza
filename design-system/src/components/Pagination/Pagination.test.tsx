import { render, fireEvent, screen } from '@testing-library/react'
import { Pagination } from './Pagination'

describe('Pagination', () => {
  it('shows the current page and total', () => {
    render(<Pagination page={2} pageCount={5} onPageChange={() => {}} />)
    expect(screen.getByText('Página 2 de 5')).toBeInTheDocument()
  })

  it('disables Anterior on the first page', () => {
    render(<Pagination page={1} pageCount={5} onPageChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Siguiente' })).toBeEnabled()
  })

  it('disables Siguiente on the last page', () => {
    render(<Pagination page={5} pageCount={5} onPageChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Siguiente' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeEnabled()
  })

  it('disables both buttons when there is only one page', () => {
    render(<Pagination page={1} pageCount={1} onPageChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Siguiente' })).toBeDisabled()
  })

  it('calls onPageChange with page - 1 / page + 1', () => {
    const onPageChange = vi.fn()
    render(<Pagination page={3} pageCount={5} onPageChange={onPageChange} />)

    fireEvent.click(screen.getByRole('button', { name: 'Anterior' }))
    expect(onPageChange).toHaveBeenCalledWith(2)

    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }))
    expect(onPageChange).toHaveBeenCalledWith(4)
  })
})
