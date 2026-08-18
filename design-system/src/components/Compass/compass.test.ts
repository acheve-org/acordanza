import { labelPoint, needlePath, orientations, wedgePath } from './compass'

// Parses the two arc endpoints out of a `wedgePath`/`needlePath` `d` string — cheap
// enough to just regex the numbers back out rather than a real SVG path parser.
function points(d: string): [number, number][] {
  return [...d.matchAll(/(-?\d+\.\d+) (-?\d+\.\d+)/g)].map(([, x, y]) => [Number(x), Number(y)])
}

describe('wedgePath', () => {
  it('sector 0 (north) straddles cx with both outer endpoints above cy', () => {
    const d = wedgePath(0, 50, 50, 40, 20)
    const [outerStart, outerEnd] = points(d)
    expect(outerStart![1]).toBeLessThan(50)
    expect(outerEnd![1]).toBeLessThan(50)
    expect(outerStart![0]).toBeLessThan(50)
    expect(outerEnd![0]).toBeGreaterThan(50)
  })

  it('sector 2 (east) straddles cy with both outer endpoints to the right of cx', () => {
    const d = wedgePath(2, 50, 50, 40, 20)
    const [outerStart, outerEnd] = points(d)
    expect(outerStart![0]).toBeGreaterThan(50)
    expect(outerEnd![0]).toBeGreaterThan(50)
  })

  it('produces a distinct path for each of the eight sectors', () => {
    const paths = orientations.map((_, i) => wedgePath(i, 50, 50, 40, 20))
    expect(new Set(paths).size).toBe(8)
  })
})

describe('needlePath', () => {
  it('sector 0 (north) points straight up from centre', () => {
    const d = needlePath(0, 50, 50, 40)
    const [tip] = points(d)
    expect(tip![0]).toBeCloseTo(50, 1)
    expect(tip![1]).toBeLessThan(50)
  })

  it('sector 2 (east) points right from centre', () => {
    const d = needlePath(2, 50, 50, 40)
    const [tip] = points(d)
    expect(tip![0]).toBeGreaterThan(50)
    expect(tip![1]).toBeCloseTo(50, 1)
  })

  it('produces a distinct path for each of the eight sectors', () => {
    const paths = orientations.map((_, i) => needlePath(i, 50, 50, 40))
    expect(new Set(paths).size).toBe(8)
  })
})

describe('labelPoint', () => {
  it('sector 0 (north) sits directly above the centre', () => {
    const { x, y } = labelPoint(0, 50, 50, 34)
    expect(x).toBeCloseTo(50, 5)
    expect(y).toBeLessThan(50)
  })

  it('sector 2 (east) sits directly right of the centre', () => {
    const { x, y } = labelPoint(2, 50, 50, 34)
    expect(y).toBeCloseTo(50, 5)
    expect(x).toBeGreaterThan(50)
  })

  it('produces a distinct point for each of the eight sectors', () => {
    const points = orientations.map((_, i) => labelPoint(i, 50, 50, 34))
    const unique = new Set(points.map((p) => `${p.x.toFixed(2)} ${p.y.toFixed(2)}`))
    expect(unique.size).toBe(8)
  })
})
