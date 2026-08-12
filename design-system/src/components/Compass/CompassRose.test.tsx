import { render, screen } from '@testing-library/react'
import { CompassRose } from './CompassRose'

describe('CompassRose', () => {
  it('renders role="img" with the Spanish label', () => {
    render(<CompassRose value="south" />)
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument()
  })

  it('sm draws one needle and no wedges', () => {
    const { container } = render(<CompassRose value="north" size="sm" />)
    expect(container.querySelectorAll('.compass-rose-needle')).toHaveLength(1)
    expect(container.querySelectorAll('.compass-wedge')).toHaveLength(0)
  })

  it('md draws eight wedges and no needle', () => {
    const { container } = render(<CompassRose value="north" size="md" />)
    expect(container.querySelectorAll('.compass-wedge')).toHaveLength(8)
    expect(container.querySelectorAll('.compass-rose-needle')).toHaveLength(0)
  })

  it('md marks the selected sector', () => {
    const { container } = render(<CompassRose value="east" size="md" />)
    expect(container.querySelectorAll('.compass-wedge-selected')).toHaveLength(1)
  })
})
