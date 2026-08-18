import { render, screen } from '@testing-library/react'
import { GradePill } from './GradePill'
import { gradeBand } from './gradeBand'

// Issue #152: this table is duplicated verbatim by the backend's
// GradeValidatorTests.cs — the duplication is deliberate, a drift detector
// between the two grammars.
describe('gradeBand', () => {
  it.each([
    ['1a', '1-5'],
    ['5c+', '1-5'],
    ['6a', '6'],
    ['6c+', '6'],
    ['7a', '7'],
    ['7c+', '7'],
    ['8a', '8-9'],
    ['9c+', '8-9'],
  ] as const)('%s -> %s', (grade, band) => {
    expect(gradeBand(grade)).toBe(band)
  })

  it.each(['0a', '10a', '6d', '6', 'a', '+', '6a++', '6 a', '6a+extra', 'V+', 'E2 5b', ''])(
    '%s is not a grade',
    (grade) => {
      expect(gradeBand(grade)).toBeNull()
    },
  )
})

describe('GradePill', () => {
  it.each([
    ['1a', undefined],
    ['6a', 'grade-band-6'],
    ['7a', 'grade-band-7'],
    ['8a', 'grade-band-8-9'],
  ] as const)('renders the grade text for %s', (grade, bandClass) => {
    render(<GradePill grade={grade} />)
    const pill = screen.getByText(grade)
    expect(pill).toBeInTheDocument()
    if (bandClass) {
      expect(pill).toHaveClass(bandClass)
    }
  })

  it('renders no band class for a non-french-scale grade', () => {
    render(<GradePill grade="V+" />)
    const pill = screen.getByText('V+')
    expect(pill).toHaveClass('grade-pill')
    expect(pill.className).toBe('grade-pill')
  })

  it('renders the grade with the obligatory grade in parentheses when both are present', () => {
    render(<GradePill grade="7a" obligatoryGrade="6b" />)
    const pill = screen.getByText('7a (6b)')
    expect(pill).toHaveClass('grade-band-7')
  })

  // Plan 34 §7's composition table.
  describe('grade/aidGrade/obligatoryGrade composition', () => {
    it('renders just the free grade when nothing else is present', () => {
      render(<GradePill grade="6c" />)
      expect(screen.getByText('6c')).toBeInTheDocument()
    })

    it('renders grade/aidGrade joined with a slash', () => {
      render(<GradePill grade="6c" aidGrade="A1" />)
      expect(screen.getByText('6c/A1')).toBeInTheDocument()
    })

    it('renders just the aid grade when there is no free grade', () => {
      render(<GradePill grade={null} aidGrade="A2" />)
      const pill = screen.getByText('A2')
      expect(pill).toBeInTheDocument()
      expect(pill.className).not.toMatch(/grade-band-/)
    })

    it('renders grade/aidGrade with the obligatory grade in parentheses', () => {
      render(<GradePill grade="6c" aidGrade="A1" obligatoryGrade="6a+" />)
      expect(screen.getByText('6c/A1 (6a+)')).toBeInTheDocument()
    })

    it('renders the muted "Sin grado" pill when both grades are absent', () => {
      render(<GradePill grade={null} />)
      const pill = screen.getByText('Sin grado')
      expect(pill).toBeInTheDocument()
      expect(pill).toHaveClass('grade-pill-empty')
    })

    it('carries no band class for an aid-only pill', () => {
      render(<GradePill grade={null} aidGrade="A1" />)
      const pill = screen.getByText('A1')
      expect(pill.className).not.toMatch(/grade-band-/)
    })
  })
})
