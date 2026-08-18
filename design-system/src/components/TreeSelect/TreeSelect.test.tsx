import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TreeSelect, type TreeSelectNode, type TreeSelectProps } from './TreeSelect'

const nodes: TreeSelectNode[] = [
  { id: 1, parentId: null, label: 'Peña Rúa' },
  { id: 2, parentId: 1, label: 'Sector Norte' },
  { id: 3, parentId: null, label: 'Otra Zona' },
]

function setup(props: Partial<TreeSelectProps> = {}) {
  const onChange = vi.fn()
  render(
    <TreeSelect nodes={nodes} value={null} onChange={onChange} placeholder="Buscar sector…" {...props} />,
  )
  return { onChange }
}

async function openAndShowAll(user: ReturnType<typeof userEvent.setup>) {
  const input = screen.getByPlaceholderText('Buscar sector…')
  await user.click(input)
  await user.click(screen.getByText('Ver todas las zonas'))
  return input
}

describe('TreeSelect', () => {
  it('renders a tree with treeitem rows carrying aria-level once Ver todas is revealed', async () => {
    const user = userEvent.setup()
    setup()
    await openAndShowAll(user)

    expect(screen.getByRole('tree')).toBeInTheDocument()
    expect(screen.getByRole('treeitem', { name: 'Peña Rúa' })).toHaveAttribute('aria-level', '1')
    expect(screen.getByRole('treeitem', { name: 'Otra Zona' })).toHaveAttribute('aria-level', '1')
  })

  it('ArrowDown/ArrowUp move aria-activedescendant over visible rows only', async () => {
    const user = userEvent.setup()
    setup({ headNodes: [{ id: 1, parentId: null, label: 'Peña Rúa' }] })
    const input = screen.getByPlaceholderText('Buscar sector…')
    await user.click(input)

    const headRow = screen.getByRole('treeitem', { name: 'Peña Rúa' })
    expect(input).toHaveAttribute('aria-activedescendant', headRow.id)

    await user.keyboard('{ArrowDown}')
    const showAll = screen.getByText('Ver todas las zonas')
    expect(input).toHaveAttribute('aria-activedescendant', showAll.id)

    // Clamped at the last visible row, not off the end.
    await user.keyboard('{ArrowDown}')
    expect(input).toHaveAttribute('aria-activedescendant', showAll.id)

    await user.keyboard('{ArrowUp}')
    expect(input).toHaveAttribute('aria-activedescendant', headRow.id)
  })

  it('ArrowRight expands and ArrowLeft collapses a node, flipping aria-expanded', async () => {
    const user = userEvent.setup()
    setup()
    await openAndShowAll(user)

    const peñaRua = screen.getByRole('treeitem', { name: 'Peña Rúa' })
    expect(peñaRua).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('treeitem', { name: 'Sector Norte' })).not.toBeInTheDocument()

    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('treeitem', { name: 'Peña Rúa' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('treeitem', { name: 'Sector Norte' })).toBeInTheDocument()

    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('treeitem', { name: 'Peña Rúa' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('treeitem', { name: 'Sector Norte' })).not.toBeInTheDocument()
  })

  it('Enter selects the active row', async () => {
    const user = userEvent.setup()
    const { onChange } = setup()
    await openAndShowAll(user)

    await user.keyboard('{Enter}')

    expect(onChange).toHaveBeenCalledWith(1)
  })

  it('Escape closes the popup and keeps focus on the input', async () => {
    const user = userEvent.setup()
    setup()
    const input = screen.getByPlaceholderText('Buscar sector…')
    await user.click(input)
    expect(screen.getByRole('tree')).toBeInTheDocument()

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('tree')).not.toBeInTheDocument()
    expect(input).toHaveFocus()
  })

  it('typing while a row is active re-filters without losing focus', async () => {
    const user = userEvent.setup()
    setup()
    const input = await openAndShowAll(user)
    await user.keyboard('{ArrowDown}')

    await user.type(input, 'norte')

    expect(input).toHaveFocus()
    expect(screen.getByRole('treeitem', { name: 'Sector Norte' })).toBeInTheDocument()
    expect(screen.queryByRole('treeitem', { name: 'Otra Zona' })).not.toBeInTheDocument()
  })

  it('renders headNodes when the query is empty, and Ver todas reveals the full root list', async () => {
    const user = userEvent.setup()
    setup({ headNodes: [{ id: 1, parentId: null, label: 'Peña Rúa (reciente)' }] })
    const input = screen.getByPlaceholderText('Buscar sector…')
    await user.click(input)

    expect(screen.getByRole('treeitem', { name: 'Peña Rúa (reciente)' })).toBeInTheDocument()
    expect(screen.queryByRole('treeitem', { name: 'Otra Zona' })).not.toBeInTheDocument()

    await user.click(screen.getByText('Ver todas las zonas'))

    expect(screen.getByRole('treeitem', { name: 'Peña Rúa' })).toBeInTheDocument()
    expect(screen.getByRole('treeitem', { name: 'Otra Zona' })).toBeInTheDocument()
  })

  it('onCreateChild fires with the clicked rows id, and with null from the foot button', async () => {
    const user = userEvent.setup()
    const onCreateChild = vi.fn()
    setup({ onCreateChild })
    await openAndShowAll(user)

    await user.click(screen.getAllByRole('button', { name: 'Añadir subsector aquí' })[0]!)
    expect(onCreateChild).toHaveBeenCalledWith(1)

    await user.click(screen.getByRole('button', { name: '+ Añadir zona nueva' }))
    expect(onCreateChild).toHaveBeenCalledWith(null)
  })
})
