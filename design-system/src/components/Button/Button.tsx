import type { ComponentPropsWithoutRef, ElementType, ReactElement } from 'react'
import './Button.css'

export type ButtonVariant = 'primary' | 'ghost'

export type ButtonProps<E extends ElementType = 'button'> = {
  /** Renders as a different element/component (e.g. react-router's `Link`) while keeping button styling. */
  as?: E
  variant?: ButtonVariant
  /** Ghost-only: tighter padding and smaller type. */
  small?: boolean
  /** Ghost-only: red border/text on hover, for destructive actions. */
  danger?: boolean
  fullWidth?: boolean
} & Omit<ComponentPropsWithoutRef<E>, 'as' | 'variant' | 'small' | 'danger' | 'fullWidth'>

export function Button<E extends ElementType = 'button'>({
  as,
  variant = 'primary',
  small = false,
  danger = false,
  fullWidth = false,
  className,
  ...rest
}: ButtonProps<E>): ReactElement {
  const Component = (as ?? 'button') as ElementType
  const classNames = [
    variant === 'primary' ? 'primary-btn' : 'ghost-btn',
    variant === 'ghost' && small ? 'small' : undefined,
    variant === 'ghost' && danger ? 'danger' : undefined,
    fullWidth ? 'full-width' : undefined,
    className,
  ]
    .filter(Boolean)
    .join(' ')

  return <Component className={classNames} {...rest} />
}
