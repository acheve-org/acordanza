import type { TreeSelectNode } from './TreeSelect'

export interface TreeIndex {
  byId: Map<number, TreeSelectNode>
  /** Children of a node, in the order they were passed to buildIndex — root children keyed by null. */
  childrenOf: Map<number | null, TreeSelectNode[]>
  /** A node's ancestors, root-first, not including the node itself. */
  ancestorsOf: Map<number, TreeSelectNode[]>
}

// Diacritics-folded, lowercased, whitespace-trimmed — no dependency, just NFD +
// stripping combining marks (the Unicode "Combining Diacritical Marks" block).
// `pena` must match `Peña` in a Spanish-only app.
export function normalise(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim()
}

export function buildIndex(nodes: TreeSelectNode[]): TreeIndex {
  const byId = new Map(nodes.map((n) => [n.id, n]))
  const childrenOf = new Map<number | null, TreeSelectNode[]>()
  for (const node of nodes) {
    const siblings = childrenOf.get(node.parentId)
    if (siblings) siblings.push(node)
    else childrenOf.set(node.parentId, [node])
  }

  const ancestorsOf = new Map<number, TreeSelectNode[]>()
  function ancestorsOfId(id: number): TreeSelectNode[] {
    const cached = ancestorsOf.get(id)
    if (cached) return cached
    const node = byId.get(id)
    const parent = node?.parentId != null ? byId.get(node.parentId) : undefined
    const chain = parent ? [...ancestorsOfId(parent.id), parent] : []
    ancestorsOf.set(id, chain)
    return chain
  }
  for (const node of nodes) ancestorsOfId(node.id)

  return { byId, childrenOf, ancestorsOf }
}

// Prefix-per-word, falling back to substring: every query word must either prefix one of
// the label's words or appear somewhere in the label. Matched against the node's own
// name, never a breadcrumb — see TreeSelect's module comment for why.
export function matches(label: string, query: string): boolean {
  const q = normalise(query)
  if (!q) return true
  const target = normalise(label)
  const targetWords = target.split(/\s+/).filter(Boolean)
  return q
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => targetWords.some((tw) => tw.startsWith(word)) || target.includes(word))
}

export interface PruneResult {
  /** Every node that should render, in the caller's `nodes` order. */
  visibleIds: Set<number>
  /** Nodes whose own name matched the query — the actual answers, not context. */
  matchedIds: Set<number>
  /** Nodes that must render expanded to keep a match reachable/visible. */
  autoExpandIds: Set<number>
}

// Keeps a match, its ancestors (as de-emphasized but still-selectable context, auto-expanded
// down to reveal the match) and its direct children (so a matched parent shows what's under
// it without the noise of unrelated branches) — drops everything else.
export function prune(nodes: TreeSelectNode[], index: TreeIndex, query: string): PruneResult {
  const q = normalise(query)
  if (!q) {
    return { visibleIds: new Set(nodes.map((n) => n.id)), matchedIds: new Set(), autoExpandIds: new Set() }
  }

  const matchedIds = new Set<number>()
  for (const node of nodes) {
    if (matches(node.label, q)) matchedIds.add(node.id)
  }

  const visibleIds = new Set<number>()
  const autoExpandIds = new Set<number>()
  for (const id of matchedIds) {
    visibleIds.add(id)
    const ancestors = index.ancestorsOf.get(id) ?? []
    for (const ancestor of ancestors) {
      visibleIds.add(ancestor.id)
      autoExpandIds.add(ancestor.id)
    }
    const children = index.childrenOf.get(id) ?? []
    if (children.length > 0) {
      autoExpandIds.add(id)
      for (const child of children) visibleIds.add(child.id)
    }
  }

  return { visibleIds, matchedIds, autoExpandIds }
}
