import type { ReactNode, Ref } from 'react'
import './List.css'

export interface ListProps {
  children: ReactNode
}

/** A vertical stack of `ListRow`s. */
export function List({ children }: ListProps) {
  return <ul className="list">{children}</ul>
}

export interface ListRowProps {
  title: ReactNode
  subtitle?: ReactNode
  /** Right-aligned content — a remove button, one or more `Button`s, or omitted entirely. */
  trailing?: ReactNode
  /** Makes the whole row clickable (e.g. opening a detail view), instead of just `trailing`'s controls. */
  onClick?: () => void
  /** Highlights the row (e.g. it's the currently map-selected area). Defaults to false. */
  selected?: boolean
  ref?: Ref<HTMLLIElement>
}

export function ListRow({ title, subtitle, trailing, onClick, selected, ref }: ListRowProps) {
  const classNames = [
    'list-row',
    onClick && 'list-row-clickable',
    selected && 'list-row-selected',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <li ref={ref} className={classNames} onClick={onClick}>
      <div className="list-row-main">
        <div className="list-row-title">{title}</div>
        {subtitle && <div className="list-row-subtitle">{subtitle}</div>}
      </div>
      {trailing && <div className="list-row-trailing">{trailing}</div>}
    </li>
  )
}
