import { render, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Combobox } from './Combobox'

describe('Combobox', () => {
  it('calls onOpen exactly once per open, not again while typing', async () => {
    const user = userEvent.setup()
    const onOpen = vi.fn()
    render(<Combobox placeholder="Buscar…" options={[]} onSelect={() => {}} onOpen={onOpen} />)

    const input = screen.getByPlaceholderText('Buscar…')
    await user.click(input) // a real focus transfer, unlike fireEvent.focus — keeps document.activeElement in sync
    expect(onOpen).toHaveBeenCalledTimes(1)

    await user.type(input, 'ab')
    expect(onOpen).toHaveBeenCalledTimes(1)
  })

  it('reports every keystroke via onQueryChange', async () => {
    const user = userEvent.setup()
    const onQueryChange = vi.fn()
    render(<Combobox placeholder="Buscar…" options={[]} onSelect={() => {}} onQueryChange={onQueryChange} />)

    await user.type(screen.getByPlaceholderText('Buscar…'), 'ab')

    expect(onQueryChange).toHaveBeenCalledWith('a')
    expect(onQueryChange).toHaveBeenCalledWith('ab')
  })

  it('selects an option by clicking it and passes its key to onSelect', () => {
    const onSelect = vi.fn()
    render(<Combobox placeholder="Buscar…" options={[{ key: 1, label: 'Peña Rúa' }]} onSelect={onSelect} />)

    fireEvent.focus(screen.getByPlaceholderText('Buscar…'))
    fireEvent.click(screen.getByRole('button', { name: 'Peña Rúa' }))

    expect(onSelect).toHaveBeenCalledWith(1)
  })

  it('closes the dropdown 150ms after losing focus', async () => {
    render(<Combobox placeholder="Buscar…" options={[{ key: 1, label: 'Peña Rúa' }]} onSelect={() => {}} />)

    const input = screen.getByPlaceholderText('Buscar…')
    fireEvent.focus(input)
    expect(screen.getByRole('button', { name: 'Peña Rúa' })).toBeInTheDocument()

    fireEvent.blur(input)
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Peña Rúa' })).not.toBeInTheDocument())
  })

  it('shows the leading option above the regular options when provided', () => {
    render(
      <Combobox
        placeholder="Buscar…"
        options={[{ key: 1, label: 'Peña Rúa' }]}
        onSelect={() => {}}
        leadingOption={{ label: 'Cualquier sector', onSelect: () => {} }}
      />,
    )

    fireEvent.focus(screen.getByPlaceholderText('Buscar…'))
    const buttons = screen.getAllByRole('button')
    expect(buttons[0]).toHaveTextContent('Cualquier sector')
    expect(buttons[1]).toHaveTextContent('Peña Rúa')
  })

  it('shows the empty message when there are no options', () => {
    render(<Combobox placeholder="Buscar…" options={[]} onSelect={() => {}} emptyMessage="Sin coincidencias" />)

    fireEvent.focus(screen.getByPlaceholderText('Buscar…'))

    expect(screen.getByText('Sin coincidencias')).toBeInTheDocument()
  })
})
