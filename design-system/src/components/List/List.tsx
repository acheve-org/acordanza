import type { ReactNode } from 'react'
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
}

export function ListRow({ title, subtitle, trailing, onClick }: ListRowProps) {
  const classNames = onClick ? 'list-row list-row-clickable' : 'list-row'

  return (
    <li className={classNames} onClick={onClick}>
      <div className="list-row-main">
        <div className="list-row-title">{title}</div>
        {subtitle && <div className="list-row-subtitle">{subtitle}</div>}
      </div>
      {trailing && <div className="list-row-trailing">{trailing}</div>}
    </li>
  )
}
