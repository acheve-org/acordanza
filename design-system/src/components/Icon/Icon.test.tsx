import { render, screen } from '@testing-library/react'
import { Icon } from './Icon'
import { icons } from './icons'

const names = Object.keys(icons) as (keyof typeof icons)[]

describe('Icon', () => {
  it.each(names)('renders exactly one <svg> for %s with no thrown error', (name) => {
    const { container } = render(<Icon name={name} />)
    expect(container.querySelectorAll('svg')).toHaveLength(1)
  })

  it('is decorative by default: aria-hidden, no role', () => {
    render(<Icon name="check" />)
    const svg = document.querySelector('svg')!
    expect(svg).toHaveAttribute('aria-hidden', 'true')
    expect(svg).not.toHaveAttribute('role')
  })

  it('switches to role="img" with the matching aria-label when given one', () => {
    render(<Icon name="check" label="Ya la has registrado" />)
    expect(screen.getByRole('img', { name: 'Ya la has registrado' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Ya la has registrado' })).not.toHaveAttribute('aria-hidden')
  })

  it.each([
    ['sm', 'icon-sm'],
    ['md', 'icon-md'],
    ['lg', 'icon-lg'],
  ] as const)('%s maps to the %s class', (size, className) => {
    const { container } = render(<Icon name="check" size={size} />)
    expect(container.querySelector('svg')).toHaveClass(className)
  })
})
