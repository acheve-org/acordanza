import { render, screen } from '@testing-library/react'
import { List, ListRow } from './List'

describe('ListRow', () => {
  it('appends list-row-selected when selected', () => {
    render(
      <List>
        <ListRow title="Peña Rúa" selected />
      </List>,
    )
    expect(screen.getByText('Peña Rúa').closest('li')).toHaveClass('list-row-selected')
  })

  it('omits list-row-selected by default', () => {
    render(
      <List>
        <ListRow title="Peña Rúa" />
      </List>,
    )
    expect(screen.getByText('Peña Rúa').closest('li')).not.toHaveClass('list-row-selected')
  })

  it('renders leading content inside list-row-leading', () => {
    render(
      <List>
        <ListRow title="Peña Rúa" leading={<span data-testid="icon" />} />
      </List>,
    )
    const leading = screen.getByTestId('icon').closest('.list-row-leading')
    expect(leading).not.toBeNull()
  })
})
