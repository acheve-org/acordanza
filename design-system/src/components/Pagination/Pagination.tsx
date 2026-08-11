import { Button } from '../Button/Button'
import './Pagination.css'

export interface PaginationProps {
  page: number
  totalCount: number
  pageSize: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, totalCount, pageSize, onPageChange }: PaginationProps) {
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  if (pageCount <= 1) return null

  return (
    <div className="pagination">
      <Button variant="ghost" small type="button" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        Anterior
      </Button>
      <span className="pagination-status">
        Página {page} de {pageCount}
      </span>
      <Button
        variant="ghost"
        small
        type="button"
        disabled={page >= pageCount}
        onClick={() => onPageChange(page + 1)}
      >
        Siguiente
      </Button>
    </div>
  )
}
