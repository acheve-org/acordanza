import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CompassPicker } from './CompassPicker'
import { orientationAbbr, orientationLabels, orientations } from './compass'

describe('CompassPicker (multiple)', () => {
  it('renders eight checkboxes named in Spanish', () => {
    render(<CompassPicker value={[]} onChange={() => {}} />)
    for (const o of orientations) {
      expect(screen.getByRole('checkbox', { name: orientationLabels[o] })).toBeInTheDocument()
    }
  });

  it('clicking an unselected sector calls onChange with it added, in compass order', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<CompassPicker value={['east']} onChange={onChange} />)

    await user.click(screen.getByRole('checkbox', { name: 'Norte' }))

    expect(onChange).toHaveBeenCalledWith(['north', 'east'])
  })

  it('clicking a selected sector calls onChange with it removed', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<CompassPicker value={['north', 'east']} onChange={onChange} />)

    await user.click(screen.getByRole('checkbox', { name: 'Norte' }))

    expect(onChange).toHaveBeenCalledWith(['east'])
  })

  it('toBeChecked() reflects value', () => {
    render(<CompassPicker value={['south']} onChange={() => {}} />)
    expect(screen.getByRole('checkbox', { name: 'Sur' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Norte' })).not.toBeChecked()
  })

  it('Tab reaches every sector in compass order', async () => {
    const user = userEvent.setup()
    render(<CompassPicker value={[]} onChange={() => {}} />)

    for (const o of orientations) {
      await user.tab()
      expect(screen.getByRole('checkbox', { name: orientationLabels[o] })).toHaveFocus()
    }
  })

  it('renders the eight abbreviations as text without changing the accessible names', () => {
    const { container } = render(<CompassPicker value={[]} onChange={() => {}} />)
    for (const o of orientations) {
      expect(container.querySelector(`.compass-wedge-label`)).toBeTruthy()
      expect(screen.getByRole('checkbox', { name: orientationLabels[o] })).toBeInTheDocument()
    }
    const labels = [...container.querySelectorAll('.compass-wedge-label')].map((el) => el.textContent)
    expect(labels).toEqual(orientations.map((o) => orientationAbbr[o]))
  })

  it('renders the multiple-only hint', () => {
    render(<CompassPicker value={[]} onChange={() => {}} />)
    expect(screen.getByText('Puedes elegir varias.')).toBeInTheDocument()
  })

  it('renders a summary of the selected labels in compass order, empty when nothing is selected', () => {
    const { rerender } = render(<CompassPicker value={['east', 'north']} onChange={() => {}} />)
    expect(screen.getByText('Norte · Este')).toBeInTheDocument()

    rerender(<CompassPicker value={[]} onChange={() => {}} />)
    expect(screen.queryByText('Norte')).not.toBeInTheDocument()
  })
})

describe('CompassPicker (multiple={false})', () => {
  it('renders eight radios', () => {
    render(<CompassPicker value={null} onChange={() => {}} multiple={false} />)
    for (const o of orientations) {
      expect(screen.getByRole('radio', { name: orientationLabels[o] })).toBeInTheDocument()
    }
  })

  it('replaces rather than accumulates', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<CompassPicker value="north" onChange={onChange} multiple={false} />)

    await user.click(screen.getByRole('radio', { name: 'Sur' }))

    expect(onChange).toHaveBeenCalledWith('south')
    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('toBeChecked() reflects value', () => {
    render(<CompassPicker value="west" onChange={() => {}} multiple={false} />)
    expect(screen.getByRole('radio', { name: 'Oeste' })).toBeChecked()
  })

  it('does not render the multiple-only hint', () => {
    render(<CompassPicker value="west" onChange={() => {}} multiple={false} />)
    expect(screen.queryByText('Puedes elegir varias.')).not.toBeInTheDocument()
  })

  it('renders a summary of the single selected label', () => {
    render(<CompassPicker value="west" onChange={() => {}} multiple={false} />)
    expect(screen.getByText('Oeste')).toBeInTheDocument()
  })
})
