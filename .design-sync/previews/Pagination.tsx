import { useState } from 'react'
import { Pagination } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
}

export function Middle() {
  const [page, setPage] = useState(3)
  return (
    <div style={canvas}>
      <Pagination page={page} pageCount={8} onPageChange={setPage} />
    </div>
  )
}

export function FirstPage() {
  const [page, setPage] = useState(1)
  return (
    <div style={canvas}>
      <Pagination page={page} pageCount={5} onPageChange={setPage} />
    </div>
  )
}

export function LastPage() {
  const [page, setPage] = useState(5)
  return (
    <div style={canvas}>
      <Pagination page={page} pageCount={5} onPageChange={setPage} />
    </div>
  )
}
