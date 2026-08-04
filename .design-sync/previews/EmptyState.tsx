import { EmptyState } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
}

export function NoRoutes() {
  return (
    <div style={canvas}>
      <EmptyState>No hay vías que coincidan. Añade la primera o amplía tu búsqueda.</EmptyState>
    </div>
  )
}

export function NoAreas() {
  return (
    <div style={canvas}>
      <EmptyState>Este sector todavía no tiene vías registradas.</EmptyState>
    </div>
  )
}
