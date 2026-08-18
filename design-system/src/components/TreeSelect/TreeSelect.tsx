import { useId, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'
import { buildIndex, normalise, prune } from './treeFilter'
import './TreeSelect.css'

export interface TreeSelectNode {
  id: number
  parentId: number | null
  label: string
  /** Rendered right-aligned on the row, e.g. a route count. */
  meta?: ReactNode
  /** Appended to the accessible name and rendered after the label, e.g. a validation status. */
  suffix?: string
  /** Rendered dimmed — e.g. an area whose whole subtree has no routes. */
  dimmed?: boolean
}

export interface TreeSelectProps {
  nodes: TreeSelectNode[]
  value: number | null
  onChange: (id: number) => void
  placeholder: string
  /** Shown in the input while closed — the caller formats it (e.g. a breadcrumb). */
  closedValue?: string
  /** Rows shown when the query is empty, before "Ver todas" — the caller formats each
   * label (e.g. a breadcrumb), since these render as a flat list, not tree position. */
  headNodes?: TreeSelectNode[]
  headEmptyHint?: ReactNode
  /** Pinned above everything, e.g. "Cualquier sector". */
  leadingOption?: { label: ReactNode; onSelect: () => void }
  /** Renders a "+" affordance per row and one at the foot; parent is implied by position. */
  onCreateChild?: (parentId: number | null) => void
  loading?: boolean
  onOpen?: () => void
}

// A tree-first, filter-in-place combobox (plan 35 §2): the popup always shows a tree, and
// typing prunes that same tree rather than swapping to a flat results list — see
// treeFilter.ts for the pure matching/pruning logic this renders. Follows the WAI-ARIA
// combobox-with-tree-popup pattern: focus never leaves the input, arrow keys move a
// roving aria-activedescendant over the popup's role="tree" rows instead.
type Row =
  | { kind: 'leading' }
  | { kind: 'head'; node: TreeSelectNode }
  | { kind: 'show-all' }
  | { kind: 'node'; node: TreeSelectNode; level: number; hasChildren: boolean; expanded: boolean }

export function TreeSelect({
  nodes,
  value,
  onChange,
  placeholder,
  closedValue = '',
  headNodes,
  headEmptyHint,
  leadingOption,
  onCreateChild,
  loading = false,
  onOpen,
}: TreeSelectProps) {
  const baseId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [manualExpanded, setManualExpanded] = useState<Set<number>>(new Set())
  const [showAllRoots, setShowAllRoots] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)

  const index = useMemo(() => buildIndex(nodes), [nodes])
  const pruned = useMemo(() => prune(nodes, index, query), [nodes, index, query])
  const filtering = normalise(query).length > 0

  const rows = useMemo<Row[]>(() => {
    const result: Row[] = []
    if (leadingOption) result.push({ kind: 'leading' })

    if (!filtering && !showAllRoots) {
      for (const node of headNodes ?? []) result.push({ kind: 'head', node })
      result.push({ kind: 'show-all' })
      return result
    }

    function walk(parentId: number | null, level: number) {
      for (const node of index.childrenOf.get(parentId) ?? []) {
        if (!pruned.visibleIds.has(node.id)) continue
        const children = index.childrenOf.get(node.id) ?? []
        const hasChildren = children.length > 0
        const expanded = filtering ? pruned.autoExpandIds.has(node.id) || manualExpanded.has(node.id) : manualExpanded.has(node.id)
        result.push({ kind: 'node', node, level, hasChildren, expanded })
        if (hasChildren && expanded) walk(node.id, level + 1)
      }
    }
    walk(null, 0)
    return result
  }, [leadingOption, filtering, showAllRoots, headNodes, index, pruned, manualExpanded])

  // Alphabetically grouped root headers (only meaningful for the un-filtered "Ver todas"
  // view — deeper levels render hierarchically under their parent, not regrouped).
  const groupLetterByRowIndex = useMemo(() => {
    if (filtering) return new Map<number, string>()
    const letters = new Map<number, string>()
    let lastLetter: string | null = null
    rows.forEach((row, i) => {
      if (row.kind !== 'node' || row.level !== 0) return
      const letter = normalise(row.node.label).charAt(0).toUpperCase() || '#'
      if (letter !== lastLetter) {
        letters.set(i, letter)
        lastLetter = letter
      }
    })
    return letters
  }, [rows, filtering])

  function rowId(i: number) {
    return `${baseId}-row-${i}`
  }

  function resetQuery() {
    setQuery('')
  }

  function openPopup() {
    if (!open) {
      setOpen(true)
      setActiveIndex(0)
      onOpen?.()
    }
  }

  function close() {
    setOpen(false)
    resetQuery()
    setShowAllRoots(false)
  }

  function select(node: TreeSelectNode) {
    onChange(node.id)
    close()
  }

  function toggleExpanded(id: number) {
    setManualExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function activate(row: Row) {
    if (row.kind === 'leading') {
      leadingOption?.onSelect()
      close()
    } else if (row.kind === 'head') {
      select(row.node)
    } else if (row.kind === 'show-all') {
      setShowAllRoots(true)
      setActiveIndex(0)
    } else {
      select(row.node)
    }
  }

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (!open) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') {
        e.preventDefault()
        openPopup()
      }
      return
    }

    switch (e.key) {
      case 'Escape':
        e.preventDefault()
        close()
        inputRef.current?.focus()
        return
      case 'ArrowDown':
        e.preventDefault()
        setActiveIndex((i) => Math.min(i + 1, rows.length - 1))
        return
      case 'ArrowUp':
        e.preventDefault()
        setActiveIndex((i) => Math.max(i - 1, 0))
        return
      case 'Home':
        e.preventDefault()
        setActiveIndex(0)
        return
      case 'End':
        e.preventDefault()
        setActiveIndex(rows.length - 1)
        return
      case 'Enter': {
        e.preventDefault()
        const row = rows[activeIndex]
        if (row) activate(row)
        return
      }
      case 'ArrowRight': {
        const row = rows[activeIndex]
        if (row?.kind !== 'node') return
        e.preventDefault()
        if (row.hasChildren && !row.expanded) {
          toggleExpanded(row.node.id)
        } else if (row.hasChildren) {
          setActiveIndex((i) => Math.min(i + 1, rows.length - 1))
        }
        return
      }
      case 'ArrowLeft': {
        const row = rows[activeIndex]
        if (row?.kind !== 'node') return
        e.preventDefault()
        if (row.hasChildren && row.expanded) {
          toggleExpanded(row.node.id)
        } else if (row.node.parentId != null) {
          const parentRowIndex = rows.findIndex((r) => r.kind === 'node' && r.node.id === row.node.parentId)
          if (parentRowIndex >= 0) setActiveIndex(parentRowIndex)
        }
        return
      }
    }
  }

  const activeRow = rows[Math.min(activeIndex, rows.length - 1)]
  const activeDescendant = open && activeRow ? rowId(Math.min(activeIndex, rows.length - 1)) : undefined

  function rowLabel(node: TreeSelectNode) {
    return node.suffix ? `${node.label} (${node.suffix})` : node.label
  }

  return (
    <div className={open ? 'tree-select is-open' : 'tree-select'}>
      <input
        ref={inputRef}
        type="text"
        role="combobox"
        aria-expanded={open}
        aria-controls={`${baseId}-popup`}
        aria-activedescendant={activeDescendant}
        aria-autocomplete="list"
        placeholder={placeholder}
        value={open ? query : closedValue}
        onFocus={() => {
          openPopup()
          resetQuery()
        }}
        onClick={openPopup}
        onChange={(e) => {
          setQuery(e.target.value)
          setActiveIndex(0)
        }}
        onKeyDown={handleKeyDown}
        onBlur={() => setTimeout(() => close(), 150)}
      />
      {open && (
        <div id={`${baseId}-popup`} className="tree-select-popup" role="tree">
          {loading ? (
            <div className="tree-select-loading">Cargando…</div>
          ) : rows.length === 0 && !leadingOption ? (
            <div className="tree-select-empty">Sin coincidencias</div>
          ) : (
            <>
              {rows.map((row, i) => {
                const letter = groupLetterByRowIndex.get(i)
                const isHeadEmptyPoint =
                  !filtering && !showAllRoots && row.kind === 'show-all' && (headNodes?.length ?? 0) === 0 && headEmptyHint
                return (
                  <div key={rowKey(row)}>
                    {letter && <div className="tree-select-group-header">{letter}</div>}
                    {isHeadEmptyPoint && <div className="tree-select-head-hint">{headEmptyHint}</div>}
                    {renderRow(row, i)}
                  </div>
                )
              })}
              {onCreateChild && (
                <button
                  type="button"
                  tabIndex={-1}
                  className="tree-select-create-root"
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => onCreateChild(null)}
                >
                  + Añadir zona nueva
                </button>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )

  function rowKey(row: Row): string {
    if (row.kind === 'leading') return 'leading'
    if (row.kind === 'show-all') return 'show-all'
    return `${row.kind}-${row.node.id}`
  }

  function renderRow(row: Row, i: number) {
    const active = i === activeIndex
    const id = rowId(i)
    const commonProps = {
      id,
      onMouseDown: (e: MouseEvent) => e.preventDefault(),
      onMouseEnter: () => setActiveIndex(i),
    }

    if (row.kind === 'leading') {
      return (
        <div
          {...commonProps}
          role="treeitem"
          aria-level={1}
          aria-selected={value === null}
          className={active ? 'tree-select-row tree-select-leading is-active' : 'tree-select-row tree-select-leading'}
          onClick={() => activate(row)}
        >
          {leadingOption!.label}
        </div>
      )
    }

    if (row.kind === 'head') {
      return (
        <div
          {...commonProps}
          role="treeitem"
          aria-level={1}
          aria-selected={value === row.node.id}
          aria-label={rowLabel(row.node)}
          className={rowClassName(row.node, active)}
          onClick={() => activate(row)}
        >
          <span className="tree-select-row-label">
            {row.node.label}
            {row.node.suffix && <span className="tree-select-suffix"> ({row.node.suffix})</span>}
          </span>
          {row.node.meta && <span className="tree-select-meta">{row.node.meta}</span>}
        </div>
      )
    }

    if (row.kind === 'show-all') {
      return (
        <div
          {...commonProps}
          role="treeitem"
          aria-level={1}
          className={active ? 'tree-select-row tree-select-show-all is-active' : 'tree-select-row tree-select-show-all'}
          onClick={() => activate(row)}
        >
          Ver todas las zonas
        </div>
      )
    }

    // row.kind === 'node'
    const { node, level, hasChildren, expanded } = row
    return (
      <div
        {...commonProps}
        role="treeitem"
        aria-level={level + 1}
        aria-selected={value === node.id}
        aria-expanded={hasChildren ? expanded : undefined}
        aria-label={rowLabel(node)}
        className={rowClassName(node, active)}
        style={{ paddingLeft: `calc(var(--space-md) + ${level} * var(--space-lg))` }}
        onClick={() => activate(row)}
      >
        {hasChildren && (
          <button
            type="button"
            tabIndex={-1}
            className="tree-select-toggle"
            aria-label={expanded ? 'Contraer' : 'Expandir'}
            onMouseDown={(e) => e.preventDefault()}
            onClick={(e) => {
              e.stopPropagation()
              toggleExpanded(node.id)
            }}
          >
            {expanded ? '−' : '+'}
          </button>
        )}
        <span className="tree-select-row-label">
          {node.label}
          {node.suffix && <span className="tree-select-suffix"> ({node.suffix})</span>}
        </span>
        {node.meta && <span className="tree-select-meta">{node.meta}</span>}
        {onCreateChild && (
          <button
            type="button"
            tabIndex={-1}
            className="tree-select-add"
            aria-label="Añadir subsector aquí"
            title="Añadir subsector aquí"
            onMouseDown={(e) => e.preventDefault()}
            onClick={(e) => {
              e.stopPropagation()
              onCreateChild(node.id)
            }}
          >
            +
          </button>
        )}
      </div>
    )
  }

  function rowClassName(node: TreeSelectNode, active: boolean): string {
    const classes = ['tree-select-row']
    if (active) classes.push('is-active')
    if (node.dimmed) classes.push('is-dimmed')
    return classes.join(' ')
  }
}
