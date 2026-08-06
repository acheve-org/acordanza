import { useState, type ReactNode } from 'react'
import './Combobox.css'

export interface ComboboxOption {
  key: number
  label: ReactNode
}

export interface ComboboxProps {
  placeholder: string
  /** Shown in the input while closed (and the query is empty) — e.g. the current selection's label. */
  closedValue?: string
  /** Options to render in the dropdown, already filtered by the caller against the latest query. */
  options: ComboboxOption[]
  onSelect: (key: number) => void
  /** Fires on every keystroke so the caller can recompute `options`. */
  onQueryChange?: (query: string) => void
  /** Runs when the dropdown opens — e.g. to refetch options. */
  onOpen?: () => void
  emptyMessage?: string
  /** Extra option pinned above the list, e.g. a "clear selection" entry. */
  leadingOption?: { label: ReactNode; onSelect: () => void }
}

export function Combobox({
  placeholder,
  closedValue = '',
  options,
  onSelect,
  onQueryChange,
  onOpen,
  emptyMessage = 'Sin coincidencias',
  leadingOption,
}: ComboboxProps) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')

  function resetQuery() {
    setQuery('')
    onQueryChange?.('')
  }

  function handleFocus() {
    setOpen(true)
    resetQuery()
    onOpen?.()
  }

  function handleChange(value: string) {
    setQuery(value)
    onQueryChange?.(value)
  }

  function close() {
    setOpen(false)
    resetQuery()
  }

  return (
    <div className="combobox">
      <input
        type="text"
        placeholder={placeholder}
        value={open ? query : closedValue}
        onFocus={handleFocus}
        onClick={() => setOpen(true)}
        onChange={(e) => handleChange(e.target.value)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
      />
      {open && (
        <div className="combobox-list">
          {leadingOption && (
            <button
              type="button"
              className="combobox-option"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => {
                leadingOption.onSelect()
                close()
              }}
            >
              {leadingOption.label}
            </button>
          )}
          {options.length === 0 && <div className="combobox-empty">{emptyMessage}</div>}
          {options.map((o) => (
            <button
              type="button"
              key={o.key}
              className="combobox-option"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => {
                onSelect(o.key)
                close()
              }}
            >
              {o.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
