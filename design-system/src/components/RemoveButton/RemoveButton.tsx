import type { ComponentPropsWithoutRef } from 'react'
import './RemoveButton.css'

export type RemoveButtonVariant = 'plain' | 'overlay'

export interface RemoveButtonProps extends Omit<ComponentPropsWithoutRef<'button'>, 'children' | 'type'> {
  /** Accessible name — required since the button's only visible content is "×". */
  label: string
  /** `overlay`: a circular dark badge for sitting on top of media (e.g. a topo thumbnail). */
  variant?: RemoveButtonVariant
}

export function RemoveButton({ label, variant = 'plain', className, ...rest }: RemoveButtonProps) {
  const classNames = [variant === 'overlay' ? 'remove-btn-overlay' : 'remove-btn', className].filter(Boolean).join(' ')

  return (
    <button type="button" className={classNames} aria-label={label} {...rest}>
      &times;
    </button>
  )
}
