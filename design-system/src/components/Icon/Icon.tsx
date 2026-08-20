import { icons, type IconName } from './icons'
import './Icon.css'

export interface IconProps {
  name: IconName
  size?: 'sm' | 'md' | 'lg'
  /** Accessible name. Omit for a decorative icon sitting beside visible text (the default —
   *  most call sites already carry a Spanish label); pass it when the icon is the button's
   *  only content. */
  label?: string
  className?: string
}

export function Icon({ name, size = 'md', label, className }: IconProps) {
  const classNames = ['icon', `icon-${size}`, className].filter(Boolean).join(' ')
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={classNames}
      aria-hidden={label ? undefined : true}
      role={label ? 'img' : undefined}
      aria-label={label}
    >
      {icons[name]}
    </svg>
  )
}
