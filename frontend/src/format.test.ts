import { formatDate } from './format';

describe('formatDate', () => {
  it('renders in es-ES regardless of the host locale', () => {
    expect(formatDate('2026-07-12T10:00:00Z')).toBe('12/7/2026');
  });
});
