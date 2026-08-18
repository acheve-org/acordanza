export type Orientation =
  | 'north'
  | 'northeast'
  | 'east'
  | 'southeast'
  | 'south'
  | 'southwest'
  | 'west'
  | 'northwest'

// Clockwise from north — this order is the component's geometry, not a preference.
export const orientations: Orientation[] = [
  'north',
  'northeast',
  'east',
  'southeast',
  'south',
  'southwest',
  'west',
  'northwest',
]

export const orientationLabels: Record<Orientation, string> = {
  north: 'Norte',
  northeast: 'Noreste',
  east: 'Este',
  southeast: 'Sureste',
  south: 'Sur',
  southwest: 'Suroeste',
  west: 'Oeste',
  northwest: 'Noroeste',
}

// Note the Spanish O for Oeste — not the English W.
export const orientationAbbr: Record<Orientation, string> = {
  north: 'N',
  northeast: 'NE',
  east: 'E',
  southeast: 'SE',
  south: 'S',
  southwest: 'SO',
  west: 'O',
  northwest: 'NO',
}

// Shared by wedgePath and needlePath below; the only trigonometry in the module.
const point = (cx: number, cy: number, r: number, deg: number) => {
  const rad = (deg * Math.PI) / 180
  return `${(cx + r * Math.cos(rad)).toFixed(2)} ${(cy + r * Math.sin(rad)).toFixed(2)}`
}

// Annulus segment for sector `index`, as an SVG path `d`. Kept pure and parameterised by
// radius so the 56px display rose and the 132px picker share one implementation.
export function wedgePath(index: number, cx: number, cy: number, outer: number, inner: number, gap = 1.5): string {
  const mid = -90 + 45 * index
  const a0 = mid - 22.5 + gap
  const a1 = mid + 22.5 - gap // `gap` degrees of air between wedges
  const p = (r: number, deg: number) => point(cx, cy, r, deg)
  return `M ${p(outer, a0)} A ${outer} ${outer} 0 0 1 ${p(outer, a1)} L ${p(inner, a1)} A ${inner} ${inner} 0 0 0 ${p(inner, a0)} Z`
}

// Where sector `index`'s label sits: on its centre angle, at radius `r`. Returns SVG
// coordinates rather than a path string — this feeds <text x= y=, not a `d`.
export function labelPoint(index: number, cx: number, cy: number, r: number): { x: number; y: number } {
  const mid = -90 + 45 * index
  const rad = (mid * Math.PI) / 180
  return { x: cx + r * Math.cos(rad), y: cy + r * Math.sin(rad) }
}

// A needle pointing along sector `index`'s centre angle: a kite from tip to two
// shoulders to a tail notch — the shape stays visually distinct at 18px, unlike a
// wedge, where neighbouring sectors differ only by a 45° rotation of the same blunt form.
export function needlePath(index: number, cx: number, cy: number, r: number): string {
  const mid = -90 + 45 * index
  const p = (radius: number, deg: number) => point(cx, cy, radius, deg)
  return `M ${p(r, mid)} L ${p(r * 0.75, mid + 138)} L ${p(r * 0.25, mid + 180)} L ${p(r * 0.75, mid - 138)} Z`
}
