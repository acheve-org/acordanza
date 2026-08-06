import { useEffect, useRef } from 'react'
import { Combobox } from 'acordanza-design-system'

const canvas: React.CSSProperties = {
  background: 'var(--bg)',
  color: 'var(--text)',
  padding: 24,
  borderRadius: 12,
  minHeight: 260,
}

function useAutoOpen<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  useEffect(() => {
    ref.current?.querySelector('input')?.focus()
  }, [])
  return ref
}

export function Default() {
  const ref = useAutoOpen<HTMLDivElement>()
  return (
    <div style={canvas} ref={ref}>
      <Combobox
        placeholder="Buscar aperturista…"
        options={[
          { key: 1, label: 'Marta Ruiz' },
          { key: 2, label: 'Jon Etxeberria · jon92' },
          { key: 3, label: 'Laia Puig' },
        ]}
        onSelect={() => {}}
      />
    </div>
  )
}

export function WithLeadingOption() {
  const ref = useAutoOpen<HTMLDivElement>()
  return (
    <div style={canvas} ref={ref}>
      <Combobox
        placeholder="Buscar sector…"
        closedValue="Roca Roja / Techo Sur"
        options={[
          { key: 1, label: 'Roca Roja / Placa Norte' },
          { key: 2, label: 'Roca Roja / Techo Sur' },
        ]}
        onSelect={() => {}}
        leadingOption={{ label: 'Cualquier sector', onSelect: () => {} }}
      />
    </div>
  )
}
