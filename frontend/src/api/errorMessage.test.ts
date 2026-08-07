import { errorMessage } from './errorMessage';
import { ApiError } from './client';

describe('errorMessage', () => {
  it('passes through the ApiError message', () => {
    const err = new ApiError(403, 'No tienes permiso para hacer esto');

    expect(errorMessage(err, 'fallback')).toBe('No tienes permiso para hacer esto');
  });

  it('falls back for any other error', () => {
    const err = new TypeError('Failed to fetch');

    expect(errorMessage(err, 'Ha ocurrido un error')).toBe('Ha ocurrido un error');
  });
});
