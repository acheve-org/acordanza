// Mirrors backend/Acordanza.Core/Models/AidGradeValidator.cs — keep the two in step by
// hand. Returns a boolean, not a band (unlike gradeBand): aid grades carry no accent
// colour, so there is nothing to bucket them into.
export function isAidGrade(value: string): boolean {
  return /^[AC][0-5]\+?$/.test(value.trim().toUpperCase())
}
