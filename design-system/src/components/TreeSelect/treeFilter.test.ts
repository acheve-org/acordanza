import { buildIndex, matches, normalise, prune } from './treeFilter'
import type { TreeSelectNode } from './TreeSelect'

function node(id: number, parentId: number | null, label: string): TreeSelectNode {
  return { id, parentId, label }
}

describe('normalise', () => {
  it('folds diacritics both ways', () => {
    expect(normalise('pena')).toBe(normalise('Peña'))
    expect(normalise('peña')).toBe(normalise('Pena'))
  })

  it('lowercases and trims', () => {
    expect(normalise('  Rúa  ')).toBe('rua')
  })
})

describe('matches', () => {
  it('matches a diacritics-folded query against a label', () => {
    expect(matches('Peña Rúa', 'pena')).toBe(true)
    expect(matches('Peña Rúa', 'rua')).toBe(true)
  })

  it('matches prefix-per-word across several query words', () => {
    expect(matches('Sector Norte', 'sec nor')).toBe(true)
    expect(matches('Sector Norte', 'nor sec')).toBe(true)
  })

  it('falls back to substring when a word is not a prefix of any target word', () => {
    expect(matches('Peña Rúa', 'eña')).toBe(true)
  })

  it('does not match an unrelated label', () => {
    expect(matches('Sector Norte', 'pena')).toBe(false)
  })

  it('an empty query matches everything', () => {
    expect(matches('Sector Norte', '')).toBe(true)
    expect(matches('Sector Norte', '   ')).toBe(true)
  })
})

describe('prune', () => {
  // Peña Rúa
  //   Sector Norte
  //     Vía Directa
  //   Sector Sur
  // Otra Zona
  //   Otro Sector
  const nodes = [
    node(1, null, 'Peña Rúa'),
    node(2, 1, 'Sector Norte'),
    node(3, 2, 'Vía Directa'),
    node(4, 1, 'Sector Sur'),
    node(5, null, 'Otra Zona'),
    node(6, 5, 'Otro Sector'),
  ]
  const index = buildIndex(nodes)

  it('an empty query shows every node and marks nothing as matched', () => {
    const result = prune(nodes, index, '')
    expect(result.visibleIds).toEqual(new Set(nodes.map((n) => n.id)))
    expect(result.matchedIds.size).toBe(0)
    expect(result.autoExpandIds.size).toBe(0)
  })

  it('keeps a deep match plus its ancestors, auto-expanding them, and drops unrelated branches', () => {
    const result = prune(nodes, index, 'directa')

    expect(result.matchedIds).toEqual(new Set([3]))
    expect(result.visibleIds).toEqual(new Set([1, 2, 3])) // Peña Rúa > Sector Norte > Vía Directa
    expect(result.autoExpandIds).toEqual(new Set([1, 2])) // both ancestors, expanded down to the match
    expect(result.visibleIds.has(4)).toBe(false) // Sector Sur — unrelated sibling
    expect(result.visibleIds.has(5)).toBe(false) // Otra Zona — unrelated branch
  })

  it('a matching parent stays expanded and yields its own children', () => {
    const result = prune(nodes, index, 'peña rua')

    expect(result.matchedIds).toEqual(new Set([1]))
    expect(result.autoExpandIds.has(1)).toBe(true)
    expect(result.visibleIds).toEqual(new Set([1, 2, 4])) // Peña Rúa plus its direct children
    expect(result.visibleIds.has(3)).toBe(false) // grandchild not pulled in just because the root matched
  })

  it('diacritics fold in the pruning query too', () => {
    const result = prune(nodes, index, 'rua')
    expect(result.matchedIds).toEqual(new Set([1]))
  })
})
