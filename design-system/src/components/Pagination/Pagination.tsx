import { Button } from '../Button/Button'
import './Pagination.css'

export interface PaginationProps {
  page: number
  pageCount: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, pageCount, onPageChange }: PaginationProps) {
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
