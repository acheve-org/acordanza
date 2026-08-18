import { render, screen } from '@testing-library/react'
import { CompassRose } from './CompassRose'

describe('CompassRose', () => {
  it('renders role="img" with the Spanish label', () => {
    render(<CompassRose value="south" />)
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument()
  })

  it.each(['sm', 'md'] as const)('%s draws one needle, no wedges, and the north tick', (size) => {
    const { container } = render(<CompassRose value="north" size={size} />)
    expect(container.querySelectorAll('.compass-rose-needle')).toHaveLength(1)
    expect(container.querySelectorAll('.compass-wedge')).toHaveLength(0)
    expect(container.querySelectorAll('.compass-rose-north')).toHaveLength(1)
  })
})
