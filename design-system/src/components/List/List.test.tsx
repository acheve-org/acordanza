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
})
