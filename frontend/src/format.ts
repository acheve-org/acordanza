// Every date the UI renders is a day, in Spanish, in a Spanish-only app — never
// the browser's locale, which silently renders 8/11/2026 for 11/8/2026 on an
// en-US browser. Use this instead of calling toLocaleDateString at a call site.
export function formatDate(value: string): string {
  return new Date(value).toLocaleDateString('es-ES');
}
